using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QServe.Data;
using QServe.Hubs;
using QServe.Models;

namespace QServe.Services;

/// <summary>
/// Module 5: Payment Processing.
///
/// Talks to Razorpay via plain REST calls (Orders API) rather than the official SDK — this
/// keeps the dependency footprint small and the behaviour easy to verify line-by-line.
///
/// No order in this system ever reaches OrderStatus=Approved except through this service.
/// Customer Ordering (Module 4) only ever creates PendingPayment / AwaitingVerification orders.
///
/// Two independent paths can approve an Online order — ConfirmOnlinePaymentAsync (the
/// checkout.js browser callback, fires first in the normal case) and HandleWebhookAsync (a
/// true server-to-server webhook, the redundant safety net if the browser callback never
/// arrives). Both funnel through ApproveOnlinePaymentAsync so the approval side-effects
/// (classification, popularity counters, broadcast) only ever live in one place.
/// </summary>
public class PaymentService : IPaymentService
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _config;
    private readonly HttpClient _http;
    private readonly IOrderClassifier _orderClassifier;
    private readonly IRealtimeNotifier _realtime;
    private readonly IRecommendationService _recommendationService;

    public PaymentService(ApplicationDbContext db, IConfiguration config, IHttpClientFactory httpClientFactory,
        IOrderClassifier orderClassifier, IRealtimeNotifier realtime, IRecommendationService recommendationService)
    {
        _db = db;
        _config = config;
        _orderClassifier = orderClassifier;
        _realtime = realtime;
        _recommendationService = recommendationService;
        _http = httpClientFactory.CreateClient(nameof(PaymentService));
        _http.BaseAddress = new Uri("https://api.razorpay.com/v1/");

        var keyId = _config["Razorpay:KeyId"] ?? throw new InvalidOperationException("Razorpay:KeyId not configured.");
        var keySecret = _config["Razorpay:KeySecret"] ?? throw new InvalidOperationException("Razorpay:KeySecret not configured.");
        var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{keyId}:{keySecret}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
    }

    public async Task<RazorpayCheckoutInfo> InitiateOnlinePaymentAsync(int orderId)
    {
        var order = await _db.Orders.Include(o => o.Payments).FirstOrDefaultAsync(o => o.OrderID == orderId)
            ?? throw new InvalidOperationException($"Order #{orderId} not found.");

        if (order.OrderStatus != OrderStatuses.PendingPayment)
            throw new InvalidOperationException($"Order #{orderId} is in status '{order.OrderStatus}' and is not eligible for payment.");

        var payment = order.Payment
            ?? throw new InvalidOperationException($"Order #{orderId} has no active payment record.");

        if (payment.PaymentMode != PaymentModes.Online)
            throw new InvalidOperationException($"Order #{orderId} has payment mode '{payment.PaymentMode}', not '{PaymentModes.Online}'.");

        var amountInPaise = (long)(order.TotalAmount * 100);

        // If an active Razorpay order ID already exists on this pending payment attempt, reuse it (idempotency on GET/refresh)
        if (!string.IsNullOrEmpty(payment.RazorpayOrderID) && (payment.PaymentStatus == PaymentStatuses.Processing || payment.PaymentStatus == PaymentStatuses.Pending))
        {
            return new RazorpayCheckoutInfo(
                RazorpayOrderId: payment.RazorpayOrderID,
                KeyId: _config["Razorpay:KeyId"]!,
                AmountInPaise: amountInPaise,
                Currency: "INR",
                OrderId: orderId);
        }

        var requestBody = JsonSerializer.Serialize(new
        {
            amount = amountInPaise,
            currency = "INR",
            receipt = $"order_{orderId}"
        });

        var response = await _http.PostAsync("orders",
            new StringContent(requestBody, Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();

        var responseJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var razorpayOrderId = responseJson.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Razorpay did not return an order id.");

        payment.RazorpayOrderID = razorpayOrderId;
        payment.PaymentStatus = PaymentStatuses.Processing;
        await _db.SaveChangesAsync();

        return new RazorpayCheckoutInfo(
            RazorpayOrderId: razorpayOrderId,
            KeyId: _config["Razorpay:KeyId"]!,
            AmountInPaise: amountInPaise,
            Currency: "INR",
            OrderId: orderId);
    }

    public async Task<ConfirmResult> ConfirmOnlinePaymentAsync(
        int orderId, string razorpayOrderId, string razorpayPaymentId, string razorpaySignature)
    {
        var order = await _db.Orders.Include(o => o.Payments).Include(o => o.Table)
            .FirstOrDefaultAsync(o => o.OrderID == orderId);
        if (order?.Payment is null) return ConfirmResult.OrderNotFound;

        if (order.Payment.RazorpayOrderID != razorpayOrderId)
            return ConfirmResult.OrderNotFound; // Prevent cross-order confirmation attacks

        if (order.Payment.PaymentStatus == PaymentStatuses.Received)
            return ConfirmResult.AlreadyProcessed; // idempotent — don't re-approve on a duplicate callback

        // PAY-2: Razorpay's standard checkout signature is
        //   HMAC-SHA256(key_secret, razorpay_order_id + "|" + razorpay_payment_id)
        var keySecret = _config["Razorpay:KeySecret"]!;
        var payload = $"{razorpayOrderId}|{razorpayPaymentId}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(keySecret));
        var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var computedSignature = Convert.ToHexString(computedHash).ToLowerInvariant();

        var signatureValid = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedSignature),
            Encoding.UTF8.GetBytes(razorpaySignature.ToLowerInvariant()));

        if (!signatureValid)
        {
            await WriteAuditLogAsync("Payments", order.Payment.PaymentID, "PaymentSignatureInvalid",
                oldValue: null, newValue: JsonSerializer.Serialize(new { razorpayOrderId, razorpayPaymentId }),
                performedBy: null);
            return ConfirmResult.SignatureInvalid;
        }

        await ApproveOnlinePaymentAsync(order, razorpayPaymentId, "OnlinePaymentApproved");
        return ConfirmResult.Approved;
    }

    public async Task<bool> HandleWebhookAsync(string rawRequestBody, string signatureHeader)
    {
        var webhookSecret = _config["Razorpay:WebhookSecret"]
            ?? throw new InvalidOperationException("Razorpay:WebhookSecret not configured.");

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret));
        var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawRequestBody));
        var computedSignature = Convert.ToHexString(computedHash).ToLowerInvariant();

        var signatureValid = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedSignature),
            Encoding.UTF8.GetBytes(signatureHeader.ToLowerInvariant()));

        if (!signatureValid)
        {
            await WriteAuditLogAsync("Payments", 0, "WebhookSignatureInvalid", null,
                newValue: null, performedBy: null);
            return false;
        }

        using var doc = JsonDocument.Parse(rawRequestBody);
        var eventType = doc.RootElement.TryGetProperty("event", out var eventProp) ? eventProp.GetString() : null;

        if (eventType != "payment.captured")
            return true;

        var paymentEntity = doc.RootElement.GetProperty("payload").GetProperty("payment").GetProperty("entity");
        var razorpayOrderId = paymentEntity.GetProperty("order_id").GetString();
        var razorpayPaymentId = paymentEntity.GetProperty("id").GetString();

        if (razorpayOrderId is null || razorpayPaymentId is null)
            return true;

        var payment = await _db.Payments
            .Include(p => p.Order).ThenInclude(o => o!.Table)
            .FirstOrDefaultAsync(p => p.RazorpayOrderID == razorpayOrderId);

        if (payment?.Order is null || payment.PaymentStatus == PaymentStatuses.Received)
            return true;

        // Server-side Amount Verification: Ensure captured amount matches the authoritative QServe order amount
        var capturedAmountInPaise = paymentEntity.TryGetProperty("amount", out var amtProp) && amtProp.TryGetInt64(out var amt)
            ? (long?)amt
            : null;
        var expectedAmountInPaise = (long)(payment.Amount * 100);

        if (capturedAmountInPaise.HasValue && capturedAmountInPaise.Value != expectedAmountInPaise)
        {
            await WriteAuditLogAsync("Payments", payment.PaymentID, "PaymentAmountMismatch",
                JsonSerializer.Serialize(new { expected = expectedAmountInPaise }),
                JsonSerializer.Serialize(new { received = capturedAmountInPaise }),
                performedBy: null);
            return true; // Reject approval on amount mismatch
        }

        await ApproveOnlinePaymentAsync(payment.Order, razorpayPaymentId, "OnlinePaymentApprovedViaWebhook", payment);
        return true;
    }

    public async Task MarkOnlinePaymentFailedAsync(int orderId)
    {
        var order = await _db.Orders.Include(o => o.Payments).Include(o => o.Table)
            .FirstOrDefaultAsync(o => o.OrderID == orderId);
        if (order?.Payment is null) return;

        order.Payment.PaymentStatus = PaymentStatuses.Failed;
        order.Payment.RejectionReason = "Payment window dismissed or failed";
        order.OrderStatus = OrderStatuses.PendingPayment;

        await WriteAuditLogAsync("Orders", order.OrderID, "OnlinePaymentFailed", null, null, performedBy: null);
        await _db.SaveChangesAsync();

        await _realtime.BroadcastOrderStatusUpdateAsync(new OrderStatusUpdateDto(
            order.OrderID, order.Table?.TableNumber ?? "", order.OrderStatus, DateTime.UtcNow));
    }

    public async Task AdminVerifyAsync(int paymentId, bool approve, int adminUserId, string? rejectionReason = null)
    {
        var payment = await _db.Payments.Include(p => p.Order).ThenInclude(o => o!.Table)
            .FirstOrDefaultAsync(p => p.PaymentID == paymentId)
            ?? throw new InvalidOperationException($"Payment {paymentId} not found.");

        if (payment.Order is null)
            throw new InvalidOperationException($"Payment {paymentId} has no associated order.");

        // Idempotency guard
        if (payment.PaymentStatus != PaymentStatuses.Pending)
            return;

        var targetStatus = approve ? OrderStatuses.Approved : OrderStatuses.PendingPayment;
        if (!OrderStatusStateMachine.CanTransition(payment.Order.OrderStatus, targetStatus))
            throw new InvalidOperationException($"Cannot transition Order #{payment.Order.OrderID} from '{payment.Order.OrderStatus}' to '{targetStatus}'.");

        var oldStatus = payment.Order.OrderStatus;

        if (approve)
        {
            payment.PaymentStatus = PaymentStatuses.Received;
            payment.VerifiedBy = adminUserId;
            payment.VerificationTime = DateTime.UtcNow;
            payment.Order.OrderStatus = OrderStatuses.Approved;
            payment.Order.ApprovedAt = DateTime.UtcNow;
        }
        else
        {
            payment.PaymentStatus = PaymentStatuses.Failed;
            payment.RejectionReason = string.IsNullOrWhiteSpace(rejectionReason) ? "Rejected by staff" : rejectionReason.Trim();
            payment.VerifiedBy = adminUserId;
            payment.VerificationTime = DateTime.UtcNow;
            // Preserves the order, items, and total — transitions to PendingPayment to allow customer to pay again
            payment.Order.OrderStatus = OrderStatuses.PendingPayment;
        }

        await WriteAuditLogAsync("Payments", payment.PaymentID,
            approve ? "PaymentVerified" : "PaymentRejected",
            JsonSerializer.Serialize(new { OrderStatus = oldStatus }),
            JsonSerializer.Serialize(new { OrderStatus = payment.Order.OrderStatus, RejectionReason = payment.RejectionReason }),
            adminUserId);

        await _db.SaveChangesAsync();

        if (approve)
        {
            await _orderClassifier.ClassifyAndSaveAsync(payment.Order.OrderID);
            await _recommendationService.OnOrderApprovedAsync(payment.Order.OrderID);
        }

        await _realtime.BroadcastOrderStatusUpdateAsync(new OrderStatusUpdateDto(
            payment.Order.OrderID, payment.Order.Table?.TableNumber ?? "", payment.Order.OrderStatus,
            payment.Order.ApprovedAt ?? DateTime.UtcNow));
    }

    public async Task<Payment> InitiateRepaymentAsync(int orderId, string paymentMode)
    {
        if (paymentMode is not (PaymentModes.Online or PaymentModes.Cash or PaymentModes.Card))
            throw new ArgumentException("Invalid payment mode.", nameof(paymentMode));

        var order = await _db.Orders
            .Include(o => o.Table)
            .Include(o => o.OrderItems).ThenInclude(oi => oi.Item)
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.OrderID == orderId)
            ?? throw new InvalidOperationException($"Order #{orderId} not found.");

        if (order.OrderStatus != OrderStatuses.PendingPayment)
        {
            throw new InvalidOperationException($"Order #{orderId} is {order.OrderStatus.ToLowerInvariant()} and can no longer be paid.");
        }

        // Server-side recalculate and validate total and availability from database items
        var itemIds = order.OrderItems.Select(oi => oi.ItemID).ToList();
        var currentItems = await _db.MenuItems
            .Include(m => m.Category)
            .Where(m => itemIds.Contains(m.ItemID))
            .ToDictionaryAsync(m => m.ItemID);

        decimal total = 0;
        foreach (var item in order.OrderItems)
        {
            if (!currentItems.TryGetValue(item.ItemID, out var dbItem)
                || !dbItem.IsAvailable
                || (dbItem.Category != null && !dbItem.Category.IsActive))
            {
                var itemName = item.Item?.Name ?? dbItem?.Name ?? $"Item #{item.ItemID}";
                throw new InvalidOperationException($"Item \"{itemName}\" is no longer available. Please inform staff or place a new order.");
            }

            item.UnitPrice = dbItem.Price;
            total += item.UnitPrice * item.Quantity;
        }
        order.TotalAmount = total;

        // Create new Payment attempt, preserving previous payments as history
        var newPayment = new Payment
        {
            OrderID = order.OrderID,
            PaymentMode = paymentMode,
            PaymentStatus = PaymentStatuses.Pending,
            Amount = total,
            CreatedAt = DateTime.UtcNow
        };

        order.Payments.Add(newPayment);
        order.OrderStatus = paymentMode == PaymentModes.Online
            ? OrderStatuses.PendingPayment
            : OrderStatuses.AwaitingVerification;

        await WriteAuditLogAsync("Payments", order.OrderID, "RepaymentInitiated",
            null,
            JsonSerializer.Serialize(new { OrderID = order.OrderID, PaymentMode = paymentMode, Amount = total }),
            null);

        await _db.SaveChangesAsync();

        if (paymentMode != PaymentModes.Online)
        {
            await _realtime.BroadcastPaymentPendingAsync(new PaymentPendingDto(
                newPayment.PaymentID, order.Table?.TableNumber ?? "", paymentMode, total));
        }

        await _realtime.BroadcastOrderStatusUpdateAsync(new OrderStatusUpdateDto(
            order.OrderID, order.Table?.TableNumber ?? "", order.OrderStatus, DateTime.UtcNow));

        return newPayment;
    }

    public async Task RefundAsync(int paymentId, int adminUserId)
    {
        var payment = await _db.Payments.Include(p => p.Order)
            .FirstOrDefaultAsync(p => p.PaymentID == paymentId)
            ?? throw new InvalidOperationException($"Payment {paymentId} not found.");

        if (payment.PaymentStatus != PaymentStatuses.Received)
            throw new InvalidOperationException("Only a received payment can be refunded.");

        if (payment.PaymentMode == PaymentModes.Online)
        {
            if (string.IsNullOrEmpty(payment.RazorpayPaymentID))
                throw new InvalidOperationException("No Razorpay payment id on record — cannot refund online.");

            var response = await _http.PostAsync($"payments/{payment.RazorpayPaymentID}/refund",
                new StringContent("{}", Encoding.UTF8, "application/json"));
            response.EnsureSuccessStatusCode();
        }

        var oldStatus = payment.PaymentStatus;
        payment.PaymentStatus = PaymentStatuses.Refunded;
        if (payment.Order != null)
        {
            if (OrderStatusStateMachine.CanTransition(payment.Order.OrderStatus, OrderStatuses.Cancelled))
            {
                payment.Order.OrderStatus = OrderStatuses.Cancelled;
            }
        }

        await WriteAuditLogAsync("Payments", payment.PaymentID, "PaymentRefunded",
            JsonSerializer.Serialize(new { PaymentStatus = oldStatus }),
            JsonSerializer.Serialize(new { PaymentStatus = payment.PaymentStatus }),
            adminUserId);

        await _db.SaveChangesAsync();

        if (payment.Order != null)
        {
            await _realtime.BroadcastOrderStatusUpdateAsync(new OrderStatusUpdateDto(
                payment.Order.OrderID, payment.Order.Table?.TableNumber ?? "", payment.Order.OrderStatus, DateTime.UtcNow));
        }
    }

    private async Task ApproveOnlinePaymentAsync(Order order, string razorpayPaymentId, string auditAction, Payment? payment = null)
    {
        payment ??= order.Payment ?? throw new InvalidOperationException($"Order {order.OrderID} has no Payment record.");

        if (!OrderStatusStateMachine.CanTransition(order.OrderStatus, OrderStatuses.Approved))
            return;

        var isInMemory = _db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory";
        var transaction = isInMemory ? null : await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

        try
        {
            var currentStatus = await _db.Payments.Where(p => p.PaymentID == payment.PaymentID).Select(p => p.PaymentStatus).FirstOrDefaultAsync();
            if (currentStatus == PaymentStatuses.Received) return;

            payment.PaymentStatus = PaymentStatuses.Received;
            payment.RazorpayPaymentID = razorpayPaymentId;
            payment.VerificationTime = DateTime.UtcNow;
            order.OrderStatus = OrderStatuses.Approved;
            order.ApprovedAt = DateTime.UtcNow;

            await WriteAuditLogAsync("Orders", order.OrderID, auditAction, null,
                JsonSerializer.Serialize(new { order.OrderStatus }), performedBy: null);

            await _db.SaveChangesAsync();
            if (transaction != null) await transaction.CommitAsync();
        }
        finally
        {
            if (transaction != null) await transaction.DisposeAsync();
        }

        await _orderClassifier.ClassifyAndSaveAsync(order.OrderID);
        await _recommendationService.OnOrderApprovedAsync(order.OrderID);

        await _realtime.BroadcastOrderStatusUpdateAsync(new OrderStatusUpdateDto(
            order.OrderID, order.Table?.TableNumber ?? "", order.OrderStatus, order.ApprovedAt ?? DateTime.UtcNow));
    }

    private async Task WriteAuditLogAsync(string entityType, int entityId, string action,
        string? oldValue, string? newValue, int? performedBy)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            EntityType = entityType,
            EntityID = entityId,
            Action = action,
            OldValue = oldValue,
            NewValue = newValue,
            PerformedBy = performedBy,
            Timestamp = DateTime.UtcNow
        });
        await Task.CompletedTask;
    }
}

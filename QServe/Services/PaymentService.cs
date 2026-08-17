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
/// keeps the dependency footprint small and the behaviour easy to verify line-by-line. Swap in
/// the official Razorpay .NET SDK later if you'd rather not maintain the HTTP calls by hand;
/// the interface (IPaymentService) is the seam, nothing else in the app needs to change.
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
        var order = await _db.Orders.Include(o => o.Payment).FirstOrDefaultAsync(o => o.OrderID == orderId)
            ?? throw new InvalidOperationException($"Order {orderId} not found.");

        if (order.Payment is null || order.Payment.PaymentMode != PaymentModes.Online)
            throw new InvalidOperationException($"Order {orderId} is not an online-payment order.");

        var amountInPaise = (long)(order.TotalAmount * 100);

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

        order.Payment.RazorpayOrderID = razorpayOrderId;
        order.Payment.PaymentStatus = PaymentStatuses.Processing;
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
        var order = await _db.Orders.Include(o => o.Payment).Include(o => o.Table)
            .FirstOrDefaultAsync(o => o.OrderID == orderId);
        if (order?.Payment is null) return ConfirmResult.OrderNotFound;

        if (order.Payment.PaymentStatus == PaymentStatuses.Received)
            return ConfirmResult.AlreadyProcessed; // idempotent — don't re-approve on a duplicate callback

        // PAY-2: Razorpay's standard checkout signature is
        //   HMAC-SHA256(key_secret, razorpay_order_id + "|" + razorpay_payment_id)
        // Recompute it server-side — never trust the client's "payment succeeded" callback alone.
        // NOTE: this is a DIFFERENT signature scheme from the webhook below — don't reuse this
        // logic there, and don't reuse the webhook's HMAC-over-raw-body logic here.
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
            // AC: a forged/invalid signature is rejected and changes NO order status.
            return ConfirmResult.SignatureInvalid;
        }

        await ApproveOnlinePaymentAsync(order, razorpayPaymentId, "OnlinePaymentApproved");
        return ConfirmResult.Approved;
    }

    public async Task<bool> HandleWebhookAsync(string rawRequestBody, string signatureHeader)
    {
        var webhookSecret = _config["Razorpay:WebhookSecret"]
            ?? throw new InvalidOperationException("Razorpay:WebhookSecret not configured.");

        // A TRUE webhook: HMAC-SHA256 over the exact raw request body, using a secret that is
        // configured separately in Razorpay's dashboard (Settings -> Webhooks) — this is NOT
        // the same secret math as the checkout.js signature in ConfirmOnlinePaymentAsync above.
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

        // Only "payment.captured" moves anything — every other event type (order.paid,
        // payment.authorized, etc.) is acknowledged with a 200 but otherwise ignored. Razorpay
        // retries webhooks that don't return 2xx, so returning true here for events we don't
        // act on prevents pointless retries, not just missed ones.
        if (eventType != "payment.captured")
            return true;

        var paymentEntity = doc.RootElement.GetProperty("payload").GetProperty("payment").GetProperty("entity");
        var razorpayOrderId = paymentEntity.GetProperty("order_id").GetString();
        var razorpayPaymentId = paymentEntity.GetProperty("id").GetString();

        if (razorpayOrderId is null || razorpayPaymentId is null)
            return true; // malformed payload despite a valid signature — nothing sane to act on

        var payment = await _db.Payments
            .Include(p => p.Order).ThenInclude(o => o!.Table)
            .FirstOrDefaultAsync(p => p.RazorpayOrderID == razorpayOrderId);

        // Idempotent against ConfirmOnlinePaymentAsync (or a duplicate webhook delivery —
        // Razorpay explicitly does not guarantee exactly-once delivery) already having approved
        // this same payment.
        if (payment?.Order is null || payment.PaymentStatus == PaymentStatuses.Received)
            return true;

        await ApproveOnlinePaymentAsync(payment.Order, razorpayPaymentId, "OnlinePaymentApprovedViaWebhook", payment);
        return true;
    }

    public async Task MarkOnlinePaymentFailedAsync(int orderId)
    {
        var order = await _db.Orders.Include(o => o.Payment).Include(o => o.Table)
            .FirstOrDefaultAsync(o => o.OrderID == orderId);
        if (order?.Payment is null) return;

        // PAY-9: gateway failure cancels the order.
        order.Payment.PaymentStatus = PaymentStatuses.Failed;
        order.OrderStatus = OrderStatuses.Cancelled;

        await WriteAuditLogAsync("Orders", order.OrderID, "OnlinePaymentFailed", null, null, performedBy: null);
        await _db.SaveChangesAsync();

        await _realtime.BroadcastOrderStatusUpdateAsync(new OrderStatusUpdateDto(
            order.OrderID, order.Table?.TableNumber ?? "", order.OrderStatus, DateTime.UtcNow));
    }

    public async Task AdminVerifyAsync(int paymentId, bool approve, int adminUserId)
    {
        var payment = await _db.Payments.Include(p => p.Order).ThenInclude(o => o!.Table)
            .FirstOrDefaultAsync(p => p.PaymentID == paymentId)
            ?? throw new InvalidOperationException($"Payment {paymentId} not found.");

        if (payment.Order is null)
            throw new InvalidOperationException($"Payment {paymentId} has no associated order.");

        // Idempotency guard — a double-click shouldn't re-process an already-decided payment.
        if (payment.PaymentStatus != PaymentStatuses.Pending)
            return;

        var oldStatus = payment.Order.OrderStatus;

        if (approve)
        {
            // PAY-7
            payment.PaymentStatus = PaymentStatuses.Received;
            payment.VerifiedBy = adminUserId;
            payment.VerificationTime = DateTime.UtcNow;
            payment.Order.OrderStatus = OrderStatuses.Approved;
            payment.Order.ApprovedAt = DateTime.UtcNow;
        }
        else
        {
            // PAY-8. Also stamp VerifiedBy/VerificationTime here, not just on approval — the
            // Module 10 Payment Verification Log report needs a complete record of every
            // admin decision, not only the approved half of them. PaymentStatuses has no
            // dedicated "Rejected" value (fixed to the four states in the Module 1 schema),
            // so a rejection is recorded as Failed — semantically accurate (the payment was
            // not accepted) and distinguishable from Received in the report via PaymentStatus.
            payment.PaymentStatus = PaymentStatuses.Failed;
            payment.VerifiedBy = adminUserId;
            payment.VerificationTime = DateTime.UtcNow;
            payment.Order.OrderStatus = OrderStatuses.Cancelled;
        }

        await WriteAuditLogAsync("Payments", payment.PaymentID,
            approve ? "PaymentVerified" : "PaymentRejected",
            JsonSerializer.Serialize(new { OrderStatus = oldStatus }),
            JsonSerializer.Serialize(new { OrderStatus = payment.Order.OrderStatus }),
            adminUserId);

        await _db.SaveChangesAsync();

        // AI-1/AI-6: classify only on approval — a rejected/cancelled order never reaches
        // the kitchen queue, so classifying it would be wasted work.
        if (approve)
        {
            await _orderClassifier.ClassifyAndSaveAsync(payment.Order.OrderID);
            // REC-1: same reasoning — only a genuinely approved order counts toward popularity.
            await _recommendationService.OnOrderApprovedAsync(payment.Order.OrderID);
        }

        await _realtime.BroadcastOrderStatusUpdateAsync(new OrderStatusUpdateDto(
            payment.Order.OrderID, payment.Order.Table?.TableNumber ?? "", payment.Order.OrderStatus,
            payment.Order.ApprovedAt ?? DateTime.UtcNow));
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
        // Cash/Card refunds happen physically at the till — nothing to call out to; this just
        // records the state change and gives the transaction an audit trail either way.

        var oldStatus = payment.PaymentStatus;
        payment.PaymentStatus = PaymentStatuses.Refunded;

        await WriteAuditLogAsync("Payments", payment.PaymentID, "PaymentRefunded",
            JsonSerializer.Serialize(new { PaymentStatus = oldStatus }),
            JsonSerializer.Serialize(new { PaymentStatus = payment.PaymentStatus }),
            adminUserId);

        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// The single place an Online order actually becomes Approved — shared by
    /// ConfirmOnlinePaymentAsync (checkout.js callback) and HandleWebhookAsync (server-to-
    /// server webhook), so classification/popularity/broadcast side-effects never drift
    /// between the two confirmation paths.
    /// </summary>
    private async Task ApproveOnlinePaymentAsync(Order order, string razorpayPaymentId, string auditAction, Payment? payment = null)
    {
        payment ??= order.Payment ?? throw new InvalidOperationException($"Order {order.OrderID} has no Payment record.");

        payment.PaymentStatus = PaymentStatuses.Received;
        payment.RazorpayPaymentID = razorpayPaymentId;
        payment.VerificationTime = DateTime.UtcNow;
        order.OrderStatus = OrderStatuses.Approved;
        order.ApprovedAt = DateTime.UtcNow;

        await WriteAuditLogAsync("Orders", order.OrderID, auditAction, null,
            JsonSerializer.Serialize(new { order.OrderStatus }), performedBy: null);

        await _db.SaveChangesAsync();

        // AI-1/AI-6
        await _orderClassifier.ClassifyAndSaveAsync(order.OrderID);
        // REC-1
        await _recommendationService.OnOrderApprovedAsync(order.OrderID);

        // Module 7
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
        await Task.CompletedTask; // kept async-shaped in case this becomes a separate audit store later
    }
}

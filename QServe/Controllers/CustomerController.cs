using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QServe.Data;
using QServe.Hubs;
using QServe.Models;
using QServe.Services;

namespace QServe.Controllers;

/// <summary>
/// Module 4: Customer Ordering. No login — access is table-scoped via the signed QR token
/// (Module 3). Cart lives in session storage since there's no persistent customer identity.
/// </summary>
[AllowAnonymous]
[Route("order")]
public class CustomerController : Controller
{
    private const int MaxLineQuantity = 50;

    private readonly ApplicationDbContext _db;
    private readonly IQrCodeService _qrCodeService;
    private readonly IRealtimeNotifier _realtime;
    private readonly IRecommendationService _recommendationService;
    private readonly IPaymentService? _paymentService;

    public CustomerController(
        ApplicationDbContext db,
        IQrCodeService qrCodeService,
        IRealtimeNotifier realtime,
        IRecommendationService recommendationService,
        IPaymentService? paymentService = null)
    {
        _db = db;
        _qrCodeService = qrCodeService;
        _realtime = realtime;
        _recommendationService = recommendationService;
        _paymentService = paymentService;
    }

    // ---- Menu ----

    [HttpGet("table/{tableId:int}")]
    public async Task<IActionResult> Menu(int tableId, string? token = null)
    {
        token ??= HttpContext.Session.GetString(TableSession.TokenKey(tableId));

        if (string.IsNullOrEmpty(token) || !await _qrCodeService.ValidateTokenAsync(tableId, token))
            return View("InvalidTable");

        var table = await _db.RestaurantTables.FindAsync(tableId);
        if (table is null || !table.IsActive)
            return View("InvalidTable");

        HttpContext.Session.SetString(TableSession.TokenKey(tableId), token);

        var categories = await _db.MenuCategories
            .Where(c => c.IsActive)
            .OrderBy(c => c.DisplayOrder)
            .Include(c => c.Items.Where(i => i.IsAvailable))
            .ToListAsync();

        var popularItemIds = (await _recommendationService.GetPopularItemsAsync())
            .Select(i => i.ItemID)
            .ToHashSet();

        var cart = GetCart(tableId);
        ViewBag.TableId = tableId;
        ViewBag.TableNumber = table.TableNumber;
        ViewBag.CartCount = cart.Sum(c => c.Quantity);
        ViewBag.CartTotal = cart.Sum(c => c.LineTotal);
        ViewBag.PopularItemIds = popularItemIds;
        ViewBag.PriorOrderCount = GetSessionOrderIds(tableId).Count;

        return View(categories);
    }

    // ---- Cart ----

    [HttpGet("table/{tableId:int}/cart")]
    public async Task<IActionResult> Cart(int tableId)
    {
        if (!TableSessionValid(tableId)) return View("InvalidTable");

        var table = await _db.RestaurantTables.FindAsync(tableId);

        ViewBag.TableId = tableId;
        ViewBag.TableNumber = table?.TableNumber ?? tableId.ToString();
        return View(GetCart(tableId));
    }

    [HttpPost("table/{tableId:int}/cart/add")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddToCart(int tableId, int itemId, int quantity, string? customization)
    {
        if (!TableSessionValid(tableId)) return View("InvalidTable");

        var item = await _db.MenuItems.FindAsync(itemId);

        if (item is null || !item.IsAvailable)
            return BadRequest("This item is no longer available.");

        if (quantity < 1) quantity = 1;
        if (quantity > MaxLineQuantity) quantity = MaxLineQuantity;

        var cart = GetCart(tableId);
        var existingLine = cart.FirstOrDefault(c => c.ItemID == itemId && c.Customization == customization);

        if (existingLine is not null)
            existingLine.Quantity = Math.Min(existingLine.Quantity + quantity, MaxLineQuantity);
        else
            cart.Add(new CartItem
            {
                ItemID = item.ItemID,
                Name = item.Name,
                Quantity = quantity,
                UnitPrice = item.Price,
                Customization = customization,
                ItemType = item.ItemType
            });

        SaveCart(tableId, cart);

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest" || (Request.Headers.Accept.ToString().Contains("application/json") && !Request.Headers.Accept.ToString().Contains("text/html")))
        {
            return Json(new
            {
                success = true,
                cartCount = cart.Sum(c => c.Quantity),
                total = cart.Sum(c => c.LineTotal),
                itemName = item.Name
            });
        }

        return RedirectToAction(nameof(Menu), new { tableId, token = HttpContext.Session.GetString(TableSession.TokenKey(tableId)) });
    }

    [HttpPost("table/{tableId:int}/cart/update")]
    [ValidateAntiForgeryToken]
    public IActionResult UpdateCart(int tableId, int itemId, string? customization, int quantity)
    {
        if (!TableSessionValid(tableId)) return View("InvalidTable");

        var cart = GetCart(tableId);
        var line = cart.FirstOrDefault(c => c.ItemID == itemId && c.Customization == customization);

        if (line is not null)
        {
            if (quantity <= 0) cart.Remove(line);
            else line.Quantity = Math.Min(quantity, MaxLineQuantity);
            SaveCart(tableId, cart);
        }

        return RedirectToAction(nameof(Cart), new { tableId });
    }

    [HttpPost("table/{tableId:int}/cart/remove")]
    [ValidateAntiForgeryToken]
    public IActionResult RemoveFromCart(int tableId, int itemId, string? customization)
    {
        if (!TableSessionValid(tableId)) return View("InvalidTable");

        var cart = GetCart(tableId);
        var line = cart.FirstOrDefault(c => c.ItemID == itemId && c.Customization == customization);

        if (line is not null)
        {
            cart.Remove(line);
            SaveCart(tableId, cart);
        }

        return RedirectToAction(nameof(Cart), new { tableId });
    }

    // ---- Checkout ----

    [HttpPost("table/{tableId:int}/checkout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout(int tableId, string paymentMode, string? idempotencyKey = null)
    {
        if (!TableSessionValid(tableId)) return View("InvalidTable");

        // Server-side double-submission / idempotency guard
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existingOrderIdStr = HttpContext.Session.GetString($"checkout:processed:{tableId}:{idempotencyKey}");
            if (int.TryParse(existingOrderIdStr, out var existingOrderId))
            {
                return paymentMode == PaymentModes.Online
                    ? RedirectToAction("Checkout", "Payment", new { orderId = existingOrderId })
                    : RedirectToAction(nameof(Status), new { orderId = existingOrderId });
            }
        }

        var table = await _db.RestaurantTables.FindAsync(tableId);
        if (table is null || !table.IsActive)
            return View("InvalidTable");

        if (paymentMode is not (PaymentModes.Online or PaymentModes.Cash or PaymentModes.Card))
            return BadRequest("Invalid payment mode.");

        var cart = GetCart(tableId);
        if (cart.Count == 0)
            return RedirectToAction(nameof(Cart), new { tableId });

        // Retrieve current MenuItem prices and availability directly from the database
        var itemIds = cart.Select(c => c.ItemID).Distinct().ToList();
        var dbItems = await _db.MenuItems
            .Include(i => i.Category)
            .Where(i => itemIds.Contains(i.ItemID))
            .ToDictionaryAsync(i => i.ItemID);

        // Server-side validation of item availability & category status
        foreach (var line in cart)
        {
            if (!dbItems.TryGetValue(line.ItemID, out var dbItem)
                || !dbItem.IsAvailable
                || dbItem.Category == null
                || !dbItem.Category.IsActive)
            {
                TempData["CartError"] = $"Item \"{line.Name}\" is no longer available. Please update your cart.";
                return RedirectToAction(nameof(Cart), new { tableId });
            }

            // Always enforce the authoritative database unit price
            line.UnitPrice = dbItem.Price;
            line.ItemType = dbItem.ItemType;
        }

        // Server-side recalculated subtotal and total
        var total = cart.Sum(c => c.Quantity * c.UnitPrice);

        var order = new Order
        {
            TableID = tableId,
            OrderStatus = paymentMode == PaymentModes.Online
                ? OrderStatuses.PendingPayment
                : OrderStatuses.AwaitingVerification,
            TotalAmount = total,
            CreatedAt = DateTime.UtcNow
        };

        foreach (var line in cart)
        {
            order.OrderItems.Add(new OrderItem
            {
                ItemID = line.ItemID,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice, // Server-calculated price snapshot
                Customization = line.Customization
            });
        }

        order.Payment = new Payment
        {
            PaymentMode = paymentMode,
            PaymentStatus = PaymentStatuses.Pending,
            Amount = total,
            CreatedAt = DateTime.UtcNow
        };

        var isInMemory = _db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory";
        var transaction = isInMemory ? null : await _db.Database.BeginTransactionAsync();

        try
        {
            _db.Orders.Add(order);
            await _db.SaveChangesAsync();

            _db.AuditLogs.Add(new AuditLog
            {
                EntityType = "Orders",
                EntityID = order.OrderID,
                Action = "OrderCreated",
                NewValue = JsonSerializer.Serialize(new
                {
                    order.OrderID,
                    order.TableID,
                    order.TotalAmount,
                    order.OrderStatus,
                    PaymentMode = paymentMode
                }),
                Timestamp = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            if (transaction != null)
                await transaction.CommitAsync();
        }
        catch
        {
            if (transaction != null)
                await transaction.RollbackAsync();
            throw;
        }

        ClearCart(tableId);
        AddSessionOrderId(tableId, order.OrderID);

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            HttpContext.Session.SetString($"checkout:processed:{tableId}:{idempotencyKey}", order.OrderID.ToString());
        }

        if (paymentMode != PaymentModes.Online)
        {
            await _realtime.BroadcastPaymentPendingAsync(new PaymentPendingDto(
                order.Payment.PaymentID, table.TableNumber, paymentMode, total));
        }

        return paymentMode == PaymentModes.Online
            ? RedirectToAction("Checkout", "Payment", new { orderId = order.OrderID })
            : RedirectToAction(nameof(Status), new { orderId = order.OrderID });
    }

    // ---- Repayment Action ----
    [HttpPost("repay")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Repay(int orderId, string paymentMode)
    {
        var order = await _db.Orders.Include(o => o.Table).Include(o => o.Payments).FirstOrDefaultAsync(o => o.OrderID == orderId);
        if (order is null) return NotFound();

        if (!TableSession.CanAccessOrder(HttpContext.Session, _qrCodeService, order.TableID, orderId))
            return View("InvalidTable");

        if (order.OrderStatus != OrderStatuses.PendingPayment)
        {
            TempData["StatusError"] = $"Order #{orderId} has been {order.OrderStatus.ToLowerInvariant()} and can no longer be paid.";
            return RedirectToAction(nameof(Status), new { orderId });
        }

        if (_paymentService is null)
            throw new InvalidOperationException("PaymentService is not configured.");

        try
        {
            await _paymentService.InitiateRepaymentAsync(orderId, paymentMode);
        }
        catch (InvalidOperationException ex)
        {
            TempData["RepayError"] = ex.Message;
            return RedirectToAction(nameof(Status), new { orderId });
        }

        return paymentMode == PaymentModes.Online
            ? RedirectToAction("Checkout", "Payment", new { orderId = order.OrderID })
            : RedirectToAction(nameof(Status), new { orderId = order.OrderID });
    }

    // ---- Customer Pre-KDS Order Cancellation ----
    [HttpPost("order/cancel")]
    [HttpPost("table/{tableId:int}/order/{orderId:int}/cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelOrder(int orderId, int? tableId = null, string? reason = null)
    {
        var order = await _db.Orders
            .Include(o => o.Table)
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.OrderID == orderId);

        if (order is null) return NotFound();

        var effectiveTableId = tableId ?? order.TableID;
        if (!TableSession.CanAccessOrder(HttpContext.Session, _qrCodeService, effectiveTableId, orderId))
            return View("InvalidTable");

        // Customer can ONLY cancel in pre-KDS states (PendingPayment or AwaitingVerification)
        if (!OrderStatusStateMachine.CanCustomerCancel(order.OrderStatus))
        {
            TempData["StatusError"] = $"Order #{orderId} cannot be cancelled because it has already entered kitchen processing or is resolved.";
            return RedirectToAction(nameof(Status), new { orderId });
        }

        if (!OrderStatusStateMachine.CanTransition(order.OrderStatus, OrderStatuses.Cancelled))
        {
            TempData["StatusError"] = $"Order #{orderId} cannot be transitioned to Cancelled from state '{order.OrderStatus}'.";
            return RedirectToAction(nameof(Status), new { orderId });
        }

        var oldStatus = order.OrderStatus;
        order.OrderStatus = OrderStatuses.Cancelled;

        _db.AuditLogs.Add(new AuditLog
        {
            EntityType = "Orders",
            EntityID = order.OrderID,
            Action = "CustomerOrderCancelled",
            OldValue = JsonSerializer.Serialize(new { OrderStatus = oldStatus }),
            NewValue = JsonSerializer.Serialize(new
            {
                OrderStatus = OrderStatuses.Cancelled,
                TableID = effectiveTableId,
                Reason = string.IsNullOrWhiteSpace(reason) ? "Cancelled by customer before kitchen prep" : reason.Trim()
            }),
            PerformedBy = null, // Customer context
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        await _realtime.BroadcastOrderStatusUpdateAsync(new OrderStatusUpdateDto(
            order.OrderID, order.Table?.TableNumber ?? "", order.OrderStatus, DateTime.UtcNow));

        TempData["StatusSuccess"] = "Your order has been cancelled successfully.";
        return RedirectToAction(nameof(Status), new { orderId });
    }

    // ---- Status tracking ----

    [HttpGet("table/{tableId:int}/orders")]
    public async Task<IActionResult> TableOrders(int tableId)
    {
        if (!TableSessionValid(tableId)) return View("InvalidTable");

        var orderIds = GetSessionOrderIds(tableId);
        var orders = await _db.Orders
            .Where(o => orderIds.Contains(o.OrderID))
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        var table = await _db.RestaurantTables.FindAsync(tableId);
        ViewBag.TableId = tableId;
        ViewBag.TableNumber = table?.TableNumber ?? tableId.ToString();

        return View(orders);
    }

    [HttpGet("status/{orderId:int}")]
    public async Task<IActionResult> Status(int orderId)
    {
        var order = await _db.Orders.AsNoTracking()
            .Include(o => o.Table)
            .Include(o => o.Payments)
            .Include(o => o.OrderItems).ThenInclude(oi => oi.Item)
            .FirstOrDefaultAsync(o => o.OrderID == orderId);

        if (order is null) return NotFound();

        if (!TableSession.CanAccessOrder(HttpContext.Session, _qrCodeService, order.TableID, orderId))
            return View("InvalidTable");

        ViewBag.SessionOrderCount = GetSessionOrderIds(order.TableID).Count;

        return View(order);
    }

    [HttpGet("status/{orderId:int}/json")]
    public async Task<IActionResult> StatusJson(int orderId)
    {
        var order = await _db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderID == orderId);
        if (order is null) return NotFound();

        if (!TableSession.CanAccessOrder(HttpContext.Session, _qrCodeService, order.TableID, orderId))
            return Forbid();

        return Json(new
        {
            orderId = order.OrderID,
            status = order.OrderStatus,
            updatedAt = order.ApprovedAt ?? order.CreatedAt
        });
    }

    // ---- Session cart helpers ----

    private bool TableSessionValid(int tableId) =>
        TableSession.IsTableSessionValid(HttpContext.Session, _qrCodeService, tableId);

    private List<CartItem> GetCart(int tableId)
    {
        var json = HttpContext.Session.GetString(CartKey(tableId));
        return json is null ? new List<CartItem>() : JsonSerializer.Deserialize<List<CartItem>>(json) ?? new();
    }

    private void SaveCart(int tableId, List<CartItem> cart) =>
        HttpContext.Session.SetString(CartKey(tableId), JsonSerializer.Serialize(cart));

    private void ClearCart(int tableId) => HttpContext.Session.Remove(CartKey(tableId));

    private List<int> GetSessionOrderIds(int tableId)
    {
        var json = HttpContext.Session.GetString(TableSession.OrdersKey(tableId));
        return json is null ? new List<int>() : JsonSerializer.Deserialize<List<int>>(json) ?? new();
    }

    private void AddSessionOrderId(int tableId, int orderId)
    {
        var ids = GetSessionOrderIds(tableId);
        if (!ids.Contains(orderId))
        {
            ids.Add(orderId);
            HttpContext.Session.SetString(TableSession.OrdersKey(tableId), JsonSerializer.Serialize(ids));
        }
    }

    private static string CartKey(int tableId) => $"cart:table:{tableId}";
}

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
    // Gap fix: sanity cap on any single cart line — see GAP-ANALYSIS.md's Module 4 entry.
    private const int MaxLineQuantity = 50;

    private readonly ApplicationDbContext _db;
    private readonly IQrCodeService _qrCodeService;
    private readonly IRealtimeNotifier _realtime;
    private readonly IRecommendationService _recommendationService;

    public CustomerController(ApplicationDbContext db, IQrCodeService qrCodeService, IRealtimeNotifier realtime,
        IRecommendationService recommendationService)
    {
        _db = db;
        _qrCodeService = qrCodeService;
        _realtime = realtime;
        _recommendationService = recommendationService;
    }

    // ---- Menu ----

    // CUST-1/CUST-2/CUST-3: QR scan lands here with the signed token; only available items
    // in active categories are shown.
    //
    // token is optional on this action: a fresh QR scan always supplies it, but internal
    // "back to menu" / "order more" links (Cart, Status, TableOrders) omit it and rely on the
    // session-stored copy instead — still re-validated via ValidateToken either way, so this
    // isn't a new trust boundary, just avoiding forcing the raw token into every internal URL.
    [HttpGet("table/{tableId:int}")]
    public async Task<IActionResult> Menu(int tableId, string? token = null)
    {
        token ??= HttpContext.Session.GetString(TokenKey(tableId));

        if (string.IsNullOrEmpty(token) || !_qrCodeService.ValidateToken(tableId, token))
            return View("InvalidTable");

        var table = await _db.RestaurantTables.FindAsync(tableId);
        if (table is null || !table.IsActive)
            return View("InvalidTable");

        // Remember the validated token for this session so cart/checkout actions don't need
        // it re-passed on every request, while still re-validating (incl. IsActive) each time.
        HttpContext.Session.SetString(TokenKey(tableId), token);

        var categories = await _db.MenuCategories
            .Where(c => c.IsActive)
            .OrderBy(c => c.DisplayOrder)
            .Include(c => c.Items.Where(i => i.IsAvailable))
            .ToListAsync();

        // REC-4: highlight popular items with a badge.
        var popularItemIds = (await _recommendationService.GetPopularItemsAsync())
            .Select(i => i.ItemID)
            .ToHashSet();

        ViewBag.TableId = tableId;
        ViewBag.TableNumber = table.TableNumber;
        ViewBag.CartCount = GetCart(tableId).Sum(c => c.Quantity);
        ViewBag.PopularItemIds = popularItemIds;
        // Re-order support: if this table already placed orders this session, surface a link
        // to them so a customer ordering a second round doesn't lose track of the first.
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

    // CUST-4: add item with quantity + free-text customisation.
    [HttpPost("table/{tableId:int}/cart/add")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddToCart(int tableId, int itemId, int quantity, string? customization)
    {
        if (!TableSessionValid(tableId)) return View("InvalidTable");

        var item = await _db.MenuItems.FindAsync(itemId);

        // CUST-3 (server-side enforcement — the menu view already hides unavailable items,
        // but never trust the client alone).
        if (item is null || !item.IsAvailable)
            return BadRequest("This item is no longer available.");

        if (quantity < 1) quantity = 1;
        // Gap fix: no server-side ceiling previously existed — a hand-crafted request could
        // set an absurd quantity. MaxLineQuantity is a sanity cap, not a business rule; raise
        // it if a real catering-sized order ever needs more.
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
                UnitPrice = item.Price, // snapshot now — this is also what gets persisted at checkout
                Customization = customization,
                ItemType = item.ItemType
            });

        SaveCart(tableId, cart);
        return RedirectToAction(nameof(Menu), new { tableId, token = HttpContext.Session.GetString(TokenKey(tableId)) });
    }

    // CUST-5: adjust quantity or remove (quantity <= 0 removes the line).
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
        }

        SaveCart(tableId, cart);
        return RedirectToAction(nameof(Cart), new { tableId });
    }

    // ---- Checkout ----

    // CUST-6/CUST-7: mode selection + order creation.
    //
    // This is the Module 4 <-> Module 5 boundary. Customer Ordering's job stops at creating
    // the Order/OrderItems/Payment rows in the correct starting state per the lifecycle
    // diagram — it does NOT call Razorpay, does NOT verify anything, and does NOT decide when
    // an order is "Approved". That logic belongs entirely to Module 5 (Payment Processing),
    // which should pick up any order sitting in PendingPayment/AwaitingVerification from here.
    [HttpPost("table/{tableId:int}/checkout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout(int tableId, string paymentMode)
    {
        if (!TableSessionValid(tableId)) return View("InvalidTable");

        if (paymentMode is not (PaymentModes.Online or PaymentModes.Cash or PaymentModes.Card))
            return BadRequest("Invalid payment mode.");

        var cart = GetCart(tableId);
        if (cart.Count == 0)
            return RedirectToAction(nameof(Cart), new { tableId });

        var total = cart.Sum(c => c.LineTotal);

        var order = new Order
        {
            TableID = tableId,
            // Online payments wait on the Razorpay flow Module 5 owns; Cash/Card go straight
            // to admin verification per the state diagram (Placed -> PendingPayment /
            // AwaitingVerification -> ... -> Approved).
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
                UnitPrice = line.UnitPrice, // CUST-7: price snapshot at order time, not current MenuItem.Price
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

        // Single SaveChangesAsync call = one DB transaction covering Order + OrderItems +
        // Payment together, satisfying the "atomic DB transactions for order+payment" NFR.
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        ClearCart(tableId);

        // Re-order support: remember this order against the table for the rest of the
        // session, so TableOrders (and the Status page's "your other orders" link) can find
        // every order this table placed tonight, not just the one just-placed one.
        AddSessionOrderId(tableId, order.OrderID);

        if (paymentMode != PaymentModes.Online)
        {
            // PAY-5: notify Admin live that a new Cash/Card payment needs verification —
            // instead of them having to keep the queue page open and refreshing.
            var table = await _db.RestaurantTables.FindAsync(tableId);
            await _realtime.BroadcastPaymentPendingAsync(new PaymentPendingDto(
                order.Payment.PaymentID, table?.TableNumber ?? "", paymentMode, total));
        }

        // Module 4 <-> Module 5 handoff: online payments continue into Razorpay checkout;
        // Cash/Card orders are already sitting in AwaitingVerification with nothing more for
        // the customer to do, so they go straight to status tracking.
        return paymentMode == PaymentModes.Online
            ? RedirectToAction("Checkout", "Payment", new { orderId = order.OrderID })
            : RedirectToAction(nameof(Status), new { orderId = order.OrderID });
    }

    // ---- Status tracking ----

    // Re-order support: everything this table has ordered so far this session, each linking
    // to its own live Status page. This is what "order more" actually resolves to — a second
    // (or third...) independent Order row for the same table, exactly like a second paper
    // chit in a real restaurant. Nothing here reopens or modifies an already-placed order.
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

    // CUST-8: live status page. Module 7's KitchenHub now exists — Status.cshtml subscribes
    // to the "OrderStatusUpdate" SignalR broadcast for live pushes. StatusJson below is kept
    // as a one-shot fallback fetch on page load (and would still work as a degraded-connection
    // polling target if SignalR negotiation fails) rather than deleted outright.
    [HttpGet("status/{orderId:int}")]
    public async Task<IActionResult> Status(int orderId)
    {
        var order = await _db.Orders
            .Include(o => o.Table)
            .FirstOrDefaultAsync(o => o.OrderID == orderId);

        if (order is null) return NotFound();

        // Re-order support: how many orders (including this one) has this table placed this
        // session — drives whether Status.cshtml shows "view your other orders" too.
        ViewBag.SessionOrderCount = GetSessionOrderIds(order.TableID).Count;

        return View(order);
    }

    [HttpGet("status/{orderId:int}/json")]
    public async Task<IActionResult> StatusJson(int orderId)
    {
        var order = await _db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderID == orderId);
        if (order is null) return NotFound();

        return Json(new
        {
            orderId = order.OrderID,
            status = order.OrderStatus,
            updatedAt = order.ApprovedAt ?? order.CreatedAt
        });
    }

    // ---- Session cart helpers ----

    private bool TableSessionValid(int tableId)
    {
        var storedToken = HttpContext.Session.GetString(TokenKey(tableId));
        return storedToken is not null && _qrCodeService.ValidateToken(tableId, storedToken);
    }

    private List<CartItem> GetCart(int tableId)
    {
        var json = HttpContext.Session.GetString(CartKey(tableId));
        return json is null ? new List<CartItem>() : JsonSerializer.Deserialize<List<CartItem>>(json) ?? new();
    }

    private void SaveCart(int tableId, List<CartItem> cart) =>
        HttpContext.Session.SetString(CartKey(tableId), JsonSerializer.Serialize(cart));

    private void ClearCart(int tableId) => HttpContext.Session.Remove(CartKey(tableId));

    // Re-order support: which order IDs this table has placed during the current session.
    // Session-scoped (not a DB query like "all orders for TableID today") deliberately — a
    // different party seated at the same table later shouldn't see a stranger's past orders.
    private List<int> GetSessionOrderIds(int tableId)
    {
        var json = HttpContext.Session.GetString(OrdersKey(tableId));
        return json is null ? new List<int>() : JsonSerializer.Deserialize<List<int>>(json) ?? new();
    }

    private void AddSessionOrderId(int tableId, int orderId)
    {
        var ids = GetSessionOrderIds(tableId);
        if (!ids.Contains(orderId))
        {
            ids.Add(orderId);
            HttpContext.Session.SetString(OrdersKey(tableId), JsonSerializer.Serialize(ids));
        }
    }

    private static string CartKey(int tableId) => $"cart:table:{tableId}";
    private static string TokenKey(int tableId) => $"token:table:{tableId}";
    private static string OrdersKey(int tableId) => $"orders:table:{tableId}";
}

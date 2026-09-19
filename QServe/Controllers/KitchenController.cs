using System.Security.Claims;
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
/// Module 7: Kitchen Display System (KDS). Shows only payment-approved orders, sorted by the
/// Module 6 priority queue, and lets kitchen staff advance status. Every status change
/// broadcasts live via KitchenHub.
/// </summary>
[Authorize(Roles = $"{UserRoles.Kitchen},{UserRoles.Admin},{UserRoles.Manager}")]
[Route("kitchen")]
public class KitchenController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IPriorityQueueBuilder _priorityQueueBuilder;
    private readonly IRealtimeNotifier _realtime;

    private static readonly string[] ActiveStatuses =
        { OrderStatuses.Approved, OrderStatuses.Preparing, OrderStatuses.Ready };

    public KitchenController(
        ApplicationDbContext db,
        IPriorityQueueBuilder priorityQueueBuilder,
        IRealtimeNotifier realtime)
    {
        _db = db;
        _priorityQueueBuilder = priorityQueueBuilder;
        _realtime = realtime;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] string? status = null)
    {
        var query = _db.Orders.AsNoTracking()
            .Include(o => o.Table)
            .Include(o => o.OrderItems).ThenInclude(oi => oi.Item)
            .AsQueryable();

        var todayStart = DateTime.UtcNow.Date;

        if (string.Equals(status, "Served", StringComparison.OrdinalIgnoreCase))
        {
            // Completed today
            query = query.Where(o => o.OrderStatus == OrderStatuses.Served && o.CreatedAt >= todayStart);
        }
        else if (!string.IsNullOrWhiteSpace(status) && status != "All" && ActiveStatuses.Contains(status))
        {
            query = query.Where(o => o.OrderStatus == status);
        }
        else
        {
            // Default: All active kitchen orders
            query = query.Where(o => ActiveStatuses.Contains(o.OrderStatus));
        }

        var orders = await query.OrderBy(o => o.CreatedAt).ToListAsync();

        var readyOrderIds = orders.Where(o => o.OrderStatus is OrderStatuses.Ready or OrderStatuses.Served && o.ReadyAt == null).Select(o => o.OrderID).ToList();
        if (readyOrderIds.Any())
        {
            var readyLogs = await _db.AuditLogs.AsNoTracking()
                .Where(a => a.EntityType == "Orders" && readyOrderIds.Contains(a.EntityID) && a.Action == "OrderMarkedReady")
                .GroupBy(a => a.EntityID)
                .Select(g => new { OrderID = g.Key, Timestamp = g.Max(a => a.Timestamp) })
                .ToListAsync();

            var logDict = readyLogs.ToDictionary(l => l.OrderID, l => l.Timestamp);
            foreach (var order in orders.Where(o => o.OrderStatus is OrderStatuses.Ready or OrderStatuses.Served && o.ReadyAt == null))
            {
                if (logDict.TryGetValue(order.OrderID, out var ts))
                {
                    order.ReadyAt = ts;
                }
            }
        }

        // Calculate live tab counts for kitchen staff
        var allKitchenOrders = await _db.Orders.AsNoTracking()
            .Where(o => ActiveStatuses.Contains(o.OrderStatus) || (o.OrderStatus == OrderStatuses.Served && o.CreatedAt >= todayStart))
            .Select(o => new { o.OrderStatus, o.OrderType })
            .ToListAsync();

        ViewBag.ActiveCount = allKitchenOrders.Count(s => ActiveStatuses.Contains(s.OrderStatus));
        ViewBag.ApprovedCount = allKitchenOrders.Count(s => s.OrderStatus == OrderStatuses.Approved);
        ViewBag.PreparingCount = allKitchenOrders.Count(s => s.OrderStatus == OrderStatuses.Preparing);
        ViewBag.ReadyCount = allKitchenOrders.Count(s => s.OrderStatus == OrderStatuses.Ready);
        ViewBag.ServedTodayCount = allKitchenOrders.Count(s => s.OrderStatus == OrderStatuses.Served);
        ViewBag.QuickCount = allKitchenOrders.Count(s => ActiveStatuses.Contains(s.OrderStatus) && s.OrderType == OrderTypes.Quick);
        ViewBag.RegularCount = allKitchenOrders.Count(s => ActiveStatuses.Contains(s.OrderStatus) && s.OrderType == OrderTypes.Regular);
        ViewBag.HeavyCount = allKitchenOrders.Count(s => ActiveStatuses.Contains(s.OrderStatus) && s.OrderType == OrderTypes.Heavy);
        ViewBag.CurrentStatusFilter = status ?? "Active";

        var sorted = string.Equals(status, "Served", StringComparison.OrdinalIgnoreCase)
            ? orders.OrderByDescending(o => o.ServedAt ?? o.CreatedAt).ToList()
            : _priorityQueueBuilder.Build(orders);

        return View(sorted);
    }

    [HttpPost("order/{orderId:int}/preparing")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkPreparing(int orderId) =>
        await AdvanceStatus(orderId, OrderStatuses.Preparing, "PreparationStarted");

    [HttpPost("order/{orderId:int}/ready")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkReady(int orderId) =>
        await AdvanceStatus(orderId, OrderStatuses.Ready, "OrderMarkedReady");

    [HttpPost("order/{orderId:int}/served")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkServed(int orderId) =>
        await AdvanceStatus(orderId, OrderStatuses.Served, "OrderMarkedServed");

    private async Task<IActionResult> AdvanceStatus(int orderId, string newStatus, string auditAction)
    {
        var order = await _db.Orders
            .Include(o => o.Table)
            .FirstOrDefaultAsync(o => o.OrderID == orderId);

        if (order is null) return NotFound();

        if (!OrderStatusStateMachine.CanTransition(order.OrderStatus, newStatus))
        {
            TempData["KdsError"] = $"Cannot transition Order #{orderId} from '{order.OrderStatus}' to '{newStatus}'.";
            return RedirectToAction(nameof(Index));
        }

        var oldStatus = order.OrderStatus;
        order.OrderStatus = newStatus;

        if (newStatus == OrderStatuses.Ready)
        {
            order.ReadyAt = DateTime.UtcNow;
        }
        else if (newStatus == OrderStatuses.Served)
        {
            order.ServedAt = DateTime.UtcNow;
            order.ReadyAt ??= DateTime.UtcNow;
        }

        var staffUserIdRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        int? staffUserId = int.TryParse(staffUserIdRaw, out var parsedId) ? parsedId : null;

        _db.AuditLogs.Add(new AuditLog
        {
            EntityType = "Orders",
            EntityID = order.OrderID,
            Action = auditAction,
            OldValue = JsonSerializer.Serialize(new { OrderStatus = oldStatus }),
            NewValue = JsonSerializer.Serialize(new { OrderStatus = newStatus, Timestamp = DateTime.UtcNow }),
            PerformedBy = staffUserId,
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        // KDS-5: broadcast immediately to all subscribers (customer status page, admin monitor, kitchen).
        await _realtime.BroadcastOrderStatusUpdateAsync(new OrderStatusUpdateDto(
            order.OrderID,
            order.Table?.TableNumber ?? "",
            order.OrderStatus,
            order.ServedAt ?? DateTime.UtcNow));

        return RedirectToAction(nameof(Index));
    }
}

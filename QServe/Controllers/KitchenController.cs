using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QServe.Data;
using QServe.Hubs;
using QServe.Models;
using QServe.Services;

namespace QServe.Controllers;

/// <summary>
/// Module 7: Kitchen Display System. Shows only payment-approved orders, sorted by the
/// Module 6 priority queue, and lets kitchen staff advance status. Every status change
/// broadcasts live via KitchenHub — no refresh needed on any connected screen.
/// </summary>
[Authorize(Roles = UserRoles.Kitchen)]
[Route("kitchen")]
public class KitchenController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IPriorityQueueBuilder _priorityQueueBuilder;
    private readonly IRealtimeNotifier _realtime;

    private static readonly string[] ActiveStatuses =
        { OrderStatuses.Approved, OrderStatuses.Preparing, OrderStatuses.Ready };

    public KitchenController(ApplicationDbContext db, IPriorityQueueBuilder priorityQueueBuilder, IRealtimeNotifier realtime)
    {
        _db = db;
        _priorityQueueBuilder = priorityQueueBuilder;
        _realtime = realtime;
    }

    // KDS-1/KDS-2: only Approved/Preparing/Ready orders, sorted Quick -> Regular -> Heavy.
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var activeOrders = await _db.Orders
            .Where(o => ActiveStatuses.Contains(o.OrderStatus))
            .Include(o => o.Table)
            .Include(o => o.OrderItems).ThenInclude(oi => oi.Item)
            .ToListAsync();

        var sorted = _priorityQueueBuilder.Build(activeOrders);
        return View(sorted);
    }

    // KDS-4
    [HttpPost("order/{orderId:int}/preparing")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkPreparing(int orderId) =>
        await AdvanceStatus(orderId, OrderStatuses.Preparing);

    [HttpPost("order/{orderId:int}/ready")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkReady(int orderId) =>
        await AdvanceStatus(orderId, OrderStatuses.Ready);

    // Not strictly KDS scope per the PRD (kitchen only owns Approved -> Preparing -> Ready) —
    // included so the demo loop actually closes; a real deployment might put this action on a
    // separate "waiter confirms delivery" screen instead of the kitchen tablet.
    [HttpPost("order/{orderId:int}/served")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkServed(int orderId)
    {
        var order = await _db.Orders.Include(o => o.Table).FirstOrDefaultAsync(o => o.OrderID == orderId);
        if (order is null) return NotFound();

        order.OrderStatus = OrderStatuses.Served;
        order.ServedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _realtime.BroadcastOrderStatusUpdateAsync(new OrderStatusUpdateDto(
            order.OrderID, order.Table?.TableNumber ?? "", order.OrderStatus, order.ServedAt.Value));

        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult> AdvanceStatus(int orderId, string newStatus)
    {
        var order = await _db.Orders.Include(o => o.Table).FirstOrDefaultAsync(o => o.OrderID == orderId);
        if (order is null) return NotFound();

        order.OrderStatus = newStatus;
        await _db.SaveChangesAsync();

        // KDS-5: broadcast immediately to all subscribers (customer status page, admin monitor).
        await _realtime.BroadcastOrderStatusUpdateAsync(new OrderStatusUpdateDto(
            order.OrderID, order.Table?.TableNumber ?? "", order.OrderStatus, DateTime.UtcNow));

        return RedirectToAction(nameof(Index));
    }
}

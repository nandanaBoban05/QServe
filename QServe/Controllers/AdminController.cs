using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QServe.Data;
using QServe.Models;
using QServe.Services;
using QServe.ViewModels;

namespace QServe.Controllers;

// AUTH-6: role checks enforced server-side via [Authorize], not just hidden UI links.
[Authorize(Roles = $"{UserRoles.Admin},{UserRoles.Manager}")]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IQrCodeService _qrCodeService;
    private readonly IPaymentService _paymentService;

    public AdminController(ApplicationDbContext db, IQrCodeService qrCodeService, IPaymentService paymentService)
    {
        _db = db;
        _qrCodeService = qrCodeService;
        _paymentService = paymentService;
    }

    // ADM-7: landing dashboard — today's order count, revenue, pending verifications, plus
    // (new) operational counts, a 7-day trend, and a recent-activity feed pulled from
    // AuditLogs, so this reads as a real management dashboard rather than three numbers.
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var todayStart = DateTime.UtcNow.Date;

        // Revenue-bearing figures exclude Cancelled orders, matching the Reporting module's
        // convention (see Module 10 PRD) so this dashboard's numbers won't disagree with reports.
        var todayOrders = await _db.Orders
            .Where(o => o.CreatedAt >= todayStart && o.OrderStatus != OrderStatuses.Cancelled)
            .ToListAsync();

        var pendingVerificationCount = await _db.Payments.CountAsync(p =>
            p.PaymentStatus == PaymentStatuses.Pending
            && (p.PaymentMode == PaymentModes.Cash || p.PaymentMode == PaymentModes.Card));

        var activeOrderCount = await _db.Orders.CountAsync(o =>
            o.OrderStatus != OrderStatuses.Served && o.OrderStatus != OrderStatuses.Cancelled);

        var weekStart = todayStart.AddDays(-6); // 7 days inclusive of today
        var weekOrders = await _db.Orders
            .Where(o => o.CreatedAt >= weekStart && o.OrderStatus != OrderStatuses.Cancelled)
            .Select(o => new { o.CreatedAt, o.TotalAmount })
            .ToListAsync();

        var weekTrend = Enumerable.Range(0, 7)
            .Select(offset => weekStart.AddDays(offset))
            .Select(day =>
            {
                var dayOrders = weekOrders.Where(o => o.CreatedAt.Date == day).ToList();
                return new DailyTrendPoint
                {
                    Date = day,
                    OrderCount = dayOrders.Count,
                    Revenue = dayOrders.Sum(o => o.TotalAmount)
                };
            })
            .ToList();

        var recentActivity = await _db.AuditLogs
            .Include(a => a.PerformedByUser)
            .OrderByDescending(a => a.Timestamp)
            .Take(8)
            .Select(a => new RecentActivityItem
            {
                Action = a.Action,
                EntityType = a.EntityType,
                EntityID = a.EntityID,
                PerformedByName = a.PerformedByUser != null ? a.PerformedByUser.FullName : null,
                Timestamp = a.Timestamp
            })
            .ToListAsync();

        var stats = new AdminDashboardStats
        {
            TodayOrderCount = todayOrders.Count,
            TodayRevenue = todayOrders.Sum(o => o.TotalAmount),
            PendingVerificationCount = pendingVerificationCount,
            ActiveOrderCount = activeOrderCount,
            ActiveTableCount = await _db.RestaurantTables.CountAsync(t => t.IsActive),
            MenuItemCount = await _db.MenuItems.CountAsync(),
            ActiveStaffCount = await _db.Users.CountAsync(u => u.IsActive),
            WeekTrend = weekTrend,
            RecentActivity = recentActivity
        };

        return View(stats);
    }

    // ADM-4: live monitor of all active orders (everything short of Served/Cancelled).
    [HttpGet]
    public async Task<IActionResult> Orders()
    {
        var orders = await _db.Orders
            .Where(o => o.OrderStatus != OrderStatuses.Served && o.OrderStatus != OrderStatuses.Cancelled)
            .Include(o => o.Table)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        return View(orders);
    }

    // Gap fix: refunds. Separate from the Orders monitor above because that view deliberately
    // excludes Served orders (they're no longer "active"), but a refund request often comes
    // AFTER an order has been served — so this needs its own list, not a filter tweak to Orders.
    [HttpGet]
    public async Task<IActionResult> Payments()
    {
        var payments = await _db.Payments
            .Include(p => p.Order).ThenInclude(o => o!.Table)
            .Where(p => p.PaymentStatus == PaymentStatuses.Received)
            .OrderByDescending(p => p.VerificationTime ?? p.CreatedAt)
            .Take(100) // no pagination yet — same known gap as the Reporting module's tables
            .ToListAsync();

        return View(payments);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RefundPayment(int paymentId)
    {
        var adminUserIdRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(adminUserIdRaw, out var adminUserId))
            return Unauthorized();

        await _paymentService.RefundAsync(paymentId, adminUserId);
        return RedirectToAction(nameof(Payments));
    }

    // ---- Module 3: QR Code Generation ----

    [HttpGet]
    public async Task<IActionResult> Tables()
    {
        var tables = await _db.RestaurantTables.OrderBy(t => t.TableNumber).ToListAsync();
        return View(tables);
    }

    /// <summary>Streams the current QR code PNG for a table (generates it on first request).</summary>
    [HttpGet]
    public async Task<IActionResult> TableQr(int tableId)
    {
        var table = await _db.RestaurantTables.FindAsync(tableId);
        if (table is null) return NotFound();

        var pngBytes = await _qrCodeService.GenerateForTableAsync(tableId);
        return File(pngBytes, "image/png", $"table-{table.TableNumber}-qr.png");
    }

    /// <summary>
    /// QR-3: regenerate a table's QR. Note the token is a deterministic HMAC of the table ID,
    /// so this only produces a *different* code if QrCode:SigningSecret has been rotated —
    /// rotating that secret invalidates every table's QR at once (e.g., on suspected compromise).
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegenerateQr(int tableId)
    {
        await _qrCodeService.GenerateForTableAsync(tableId);
        return RedirectToAction(nameof(Tables));
    }

    // ADM-5: add / deactivate tables.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTable(string tableNumber, int capacity)
    {
        if (string.IsNullOrWhiteSpace(tableNumber))
        {
            TempData["TableError"] = "Table number is required.";
            return RedirectToAction(nameof(Tables));
        }

        var trimmedNumber = tableNumber.Trim();

        // Gap fix: this used to silently no-op on a duplicate table number, which looked to
        // the admin like the click just didn't register. Now surfaces an actual error via
        // TempData, same pattern as the success message below.
        if (await _db.RestaurantTables.AnyAsync(t => t.TableNumber == trimmedNumber))
        {
            TempData["TableError"] = $"Table \"{trimmedNumber}\" already exists.";
            return RedirectToAction(nameof(Tables));
        }

        var table = new RestaurantTable
        {
            TableNumber = trimmedNumber,
            Capacity = capacity > 0 ? capacity : 4,
            IsActive = true,
            QRCodeData = string.Empty // populated on first TableQr/RegenerateQr call
        };

        _db.RestaurantTables.Add(table);
        await _db.SaveChangesAsync();

        TempData["TableSuccess"] = $"Table \"{table.TableNumber}\" added.";
        return RedirectToAction(nameof(Tables));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleTableActive(int tableId)
    {
        var table = await _db.RestaurantTables.FindAsync(tableId);
        if (table is null) return NotFound();

        // QR-5 relies on this: QrCodeService.ValidateToken checks IsActive, so flipping this
        // off immediately breaks that table's QR for new orders without touching order history.
        table.IsActive = !table.IsActive;
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Tables));
    }

    // ---- Module 5: Payment Processing (admin-side verification queue) ----

    [HttpGet]
    public async Task<IActionResult> PaymentQueue()
    {
        // PAY-4/PAY-5: orders awaiting manual verification (Cash/Card, not yet decided).
        var pending = await _db.Payments
            .Include(p => p.Order).ThenInclude(o => o!.Table)
            .Where(p => p.PaymentStatus == PaymentStatuses.Pending
                        && (p.PaymentMode == PaymentModes.Cash || p.PaymentMode == PaymentModes.Card))
            .OrderBy(p => p.CreatedAt)
            .ToListAsync();

        return View(pending);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyPayment(int paymentId, bool approve)
    {
        var adminUserIdRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(adminUserIdRaw, out var adminUserId))
            return Unauthorized();

        await _paymentService.AdminVerifyAsync(paymentId, approve, adminUserId);
        return RedirectToAction(nameof(PaymentQueue));
    }

    // Gap fix: AuditLogs has been written to since Module 1, but nothing ever displayed it —
    // every admin/payment action's accountability trail was invisible. Simple page-based
    // listing (no fancy paging UI) since this is an ops screen, not a public report.
    [HttpGet]
    public async Task<IActionResult> AuditLog(int page = 1)
    {
        const int pageSize = 50;
        if (page < 1) page = 1;

        var totalCount = await _db.AuditLogs.CountAsync();

        var logs = await _db.AuditLogs
            .Include(a => a.PerformedByUser)
            .OrderByDescending(a => a.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.Page = page;
        ViewBag.HasNextPage = page * pageSize < totalCount;
        ViewBag.HasPrevPage = page > 1;
        ViewBag.TotalCount = totalCount;

        return View(logs);
    }
}

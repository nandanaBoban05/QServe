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
    private readonly IConfiguration _config;

    public AdminController(
        ApplicationDbContext db,
        IQrCodeService qrCodeService,
        IPaymentService paymentService,
        IConfiguration config)
    {
        _db = db;
        _qrCodeService = qrCodeService;
        _paymentService = paymentService;
        _config = config;
    }

    // ADM-7: landing dashboard — today's order count, revenue, pending verifications, plus
    // operational counts, a 7-day trend, and a recent-activity feed pulled from AuditLogs.
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var todayStart = DateTime.UtcNow.Date;

        var todayOrdersQuery = _db.Orders.AsNoTracking()
            .Where(o => o.CreatedAt >= todayStart);

        var todayOrderCount = await todayOrdersQuery
            .CountAsync(o => o.OrderStatus != OrderStatuses.Cancelled);

        var todayRevenue = await todayOrdersQuery
            .Where(o => o.OrderStatus != OrderStatuses.Cancelled)
            .SumAsync(o => (decimal?)o.TotalAmount) ?? 0m;

        var todayCompletedCount = await todayOrdersQuery
            .CountAsync(o => o.OrderStatus == OrderStatuses.Served);

        var todayCancelledCount = await todayOrdersQuery
            .CountAsync(o => o.OrderStatus == OrderStatuses.Cancelled);

        var todayOnlineRevenue = await todayOrdersQuery
            .Where(o => o.OrderStatus != OrderStatuses.Cancelled && o.Payment != null && o.Payment.PaymentMode == PaymentModes.Online && o.Payment.PaymentStatus == PaymentStatuses.Received)
            .SumAsync(o => (decimal?)o.TotalAmount) ?? 0m;

        var todayOfflineRevenue = await todayOrdersQuery
            .Where(o => o.OrderStatus != OrderStatuses.Cancelled && o.Payment != null && o.Payment.PaymentMode != PaymentModes.Online && o.Payment.PaymentStatus == PaymentStatuses.Received)
            .SumAsync(o => (decimal?)o.TotalAmount) ?? 0m;

        var pendingVerificationCount = await _db.Payments.AsNoTracking().CountAsync(p =>
            p.PaymentStatus == PaymentStatuses.Pending
            && (p.PaymentMode == PaymentModes.Cash || p.PaymentMode == PaymentModes.Card));

        var activeOrderCount = await _db.Orders.AsNoTracking().CountAsync(o =>
            o.OrderStatus != OrderStatuses.Served && o.OrderStatus != OrderStatuses.Cancelled);

        var weekStart = todayStart.AddDays(-6);
        var weekOrders = await _db.Orders.AsNoTracking()
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

        var recentActivity = await _db.AuditLogs.AsNoTracking()
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
            TodayOrderCount = todayOrderCount,
            TodayRevenue = todayRevenue,
            TodayCompletedOrderCount = todayCompletedCount,
            TodayCancelledOrderCount = todayCancelledCount,
            TodayOnlineRevenue = todayOnlineRevenue,
            TodayOfflineRevenue = todayOfflineRevenue,
            PendingVerificationCount = pendingVerificationCount,
            ActiveOrderCount = activeOrderCount,
            ActiveTableCount = await _db.RestaurantTables.AsNoTracking().CountAsync(t => t.IsActive),
            MenuItemCount = await _db.MenuItems.AsNoTracking().CountAsync(),
            ActiveStaffCount = await _db.Users.AsNoTracking().CountAsync(u => u.IsActive),
            WeekTrend = weekTrend,
            RecentActivity = recentActivity
        };

        return View(stats);
    }

    // ADM-4: Server-side filtered and paginated Orders monitor.
    [HttpGet]
    public async Task<IActionResult> Orders([FromQuery] AdminOrderFilterViewModel filter)
    {
        var query = _db.Orders.AsNoTracking()
            .Include(o => o.Table)
            .Include(o => o.Payment)
            .AsQueryable();

        // Search: Order ID or Table Number or Notes
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            if (int.TryParse(search.TrimStart('#'), out var parsedId))
            {
                query = query.Where(o => o.OrderID == parsedId || (o.Table != null && o.Table.TableNumber.Contains(search)));
            }
            else
            {
                query = query.Where(o => (o.Table != null && o.Table.TableNumber.Contains(search)) || (o.Notes != null && o.Notes.Contains(search)));
            }
        }

        if (filter.TableId.HasValue && filter.TableId.Value > 0)
        {
            query = query.Where(o => o.TableID == filter.TableId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.OrderStatus) && filter.OrderStatus != "All")
        {
            if (filter.OrderStatus == "Active")
            {
                query = query.Where(o => o.OrderStatus != OrderStatuses.Served && o.OrderStatus != OrderStatuses.Cancelled);
            }
            else
            {
                query = query.Where(o => o.OrderStatus == filter.OrderStatus);
            }
        }

        if (!string.IsNullOrWhiteSpace(filter.PaymentStatus) && filter.PaymentStatus != "All")
        {
            query = query.Where(o => o.Payment != null && o.Payment.PaymentStatus == filter.PaymentStatus);
        }

        if (!string.IsNullOrWhiteSpace(filter.PaymentMode) && filter.PaymentMode != "All")
        {
            query = query.Where(o => o.Payment != null && o.Payment.PaymentMode == filter.PaymentMode);
        }

        if (!string.IsNullOrWhiteSpace(filter.OrderType) && filter.OrderType != "All")
        {
            query = query.Where(o => o.OrderType == filter.OrderType);
        }

        if (filter.DateFrom.HasValue)
        {
            var from = filter.DateFrom.Value.Date;
            query = query.Where(o => o.CreatedAt >= from);
        }

        if (filter.DateTo.HasValue)
        {
            var toExclusive = filter.DateTo.Value.Date.AddDays(1);
            query = query.Where(o => o.CreatedAt < toExclusive);
        }

        var totalCount = await query.CountAsync();
        var pageSize = filter.PageSize > 0 ? filter.PageSize : 20;
        var page = filter.Page > 0 ? filter.Page : 1;

        var orders = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        filter.Orders = new PagedResult<Order>
        {
            Items = orders,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };

        filter.Tables = await _db.RestaurantTables.AsNoTracking().OrderBy(t => t.TableNumber).ToListAsync();

        return View(filter);
    }

    // Complete Order Details and AuditLog Timeline view.
    [HttpGet("Admin/OrderDetails/{id:int}")]
    public async Task<IActionResult> OrderDetails(int id)
    {
        var order = await _db.Orders.AsNoTracking()
            .Include(o => o.Table)
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Item)
                    .ThenInclude(i => i!.Category)
            .Include(o => o.Payment)
                .ThenInclude(p => p!.VerifiedByUser)
            .FirstOrDefaultAsync(o => o.OrderID == id);

        if (order is null) return NotFound();

        var paymentId = order.Payment?.PaymentID ?? 0;

        var timeline = await _db.AuditLogs.AsNoTracking()
            .Include(a => a.PerformedByUser)
            .Where(a => (a.EntityType == "Orders" && a.EntityID == id)
                     || (paymentId > 0 && a.EntityType == "Payments" && a.EntityID == paymentId))
            .OrderBy(a => a.Timestamp)
            .ToListAsync();

        var vm = new AdminOrderDetailsViewModel
        {
            Order = order,
            Timeline = timeline
        };

        return View(vm);
    }

    // Server-side filtered and paginated Payments list.
    [HttpGet]
    public async Task<IActionResult> Payments([FromQuery] AdminPaymentFilterViewModel filter)
    {
        var query = _db.Payments.AsNoTracking()
            .Include(p => p.Order).ThenInclude(o => o!.Table)
            .Include(p => p.VerifiedByUser)
            .AsQueryable();

        if (filter.OrderId.HasValue && filter.OrderId.Value > 0)
        {
            query = query.Where(p => p.OrderID == filter.OrderId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            if (int.TryParse(search.TrimStart('#'), out var parsedId))
            {
                query = query.Where(p => p.OrderID == parsedId || p.PaymentID == parsedId);
            }
            else
            {
                query = query.Where(p => (p.Order != null && p.Order.Table != null && p.Order.Table.TableNumber.Contains(search))
                                      || (p.RazorpayPaymentID != null && p.RazorpayPaymentID.Contains(search))
                                      || (p.RazorpayOrderID != null && p.RazorpayOrderID.Contains(search)));
            }
        }

        if (!string.IsNullOrWhiteSpace(filter.PaymentStatus) && filter.PaymentStatus != "All")
        {
            query = query.Where(p => p.PaymentStatus == filter.PaymentStatus);
        }

        if (!string.IsNullOrWhiteSpace(filter.PaymentMode) && filter.PaymentMode != "All")
        {
            query = query.Where(p => p.PaymentMode == filter.PaymentMode);
        }

        if (filter.MinAmount.HasValue)
        {
            query = query.Where(p => p.Amount >= filter.MinAmount.Value);
        }

        if (filter.MaxAmount.HasValue)
        {
            query = query.Where(p => p.Amount <= filter.MaxAmount.Value);
        }

        if (filter.DateFrom.HasValue)
        {
            var from = filter.DateFrom.Value.Date;
            query = query.Where(p => (p.VerificationTime ?? p.CreatedAt) >= from);
        }

        if (filter.DateTo.HasValue)
        {
            var toExclusive = filter.DateTo.Value.Date.AddDays(1);
            query = query.Where(p => (p.VerificationTime ?? p.CreatedAt) < toExclusive);
        }

        var totalCount = await query.CountAsync();
        var pageSize = filter.PageSize > 0 ? filter.PageSize : 20;
        var page = filter.Page > 0 ? filter.Page : 1;

        var payments = await query
            .OrderByDescending(p => p.VerificationTime ?? p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        filter.Payments = new PagedResult<Payment>
        {
            Items = payments,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };

        return View(filter);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RefundPayment(int paymentId)
    {
        var adminUserIdRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(adminUserIdRaw, out var adminUserId))
            return Unauthorized();

        var payment = await _db.Payments.FindAsync(paymentId);
        if (payment is null) return NotFound();

        await _paymentService.RefundAsync(paymentId, adminUserId);
        TempData["PaymentMessage"] = $"Payment #{paymentId} has been refunded.";
        return RedirectToAction(nameof(Payments));
    }

    // ---- Module 3: Tables & QR Code Generation ----

    [HttpGet]
    public async Task<IActionResult> Tables([FromQuery] AdminTableFilterViewModel filter)
    {
        var query = _db.RestaurantTables.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            query = query.Where(t => t.TableNumber.Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(filter.Status) && filter.Status != "All")
        {
            if (filter.Status == "Active") query = query.Where(t => t.IsActive);
            else if (filter.Status == "Inactive") query = query.Where(t => !t.IsActive);
        }

        var totalCount = await query.CountAsync();
        var pageSize = filter.PageSize > 0 ? filter.PageSize : 20;
        var page = filter.Page > 0 ? filter.Page : 1;

        var tables = await query
            .OrderBy(t => t.TableNumber)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var baseUrl = _config["App:BaseUrl"]?.TrimEnd('/')
            ?? $"{Request.Scheme}://{Request.Host}";

        filter.Tables = new PagedResult<RestaurantTable>
        {
            Items = tables,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
        filter.BaseUrl = baseUrl;

        filter.TableQrUrls = tables.ToDictionary(
            t => t.TableID,
            t => $"{baseUrl}/order/table/{t.TableID}?token={Uri.EscapeDataString(_qrCodeService.BuildToken(t.TableID))}"
        );

        return View(filter);
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegenerateQr(int tableId)
    {
        var table = await _db.RestaurantTables.FindAsync(tableId);
        if (table is null) return NotFound();

        await _qrCodeService.GenerateForTableAsync(tableId);
        TempData["TableSuccess"] = $"QR Code regenerated for Table {table.TableNumber}.";
        return RedirectToAction(nameof(Tables));
    }

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
            QRCodeData = string.Empty
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

        table.IsActive = !table.IsActive;
        await _db.SaveChangesAsync();

        TempData["TableSuccess"] = $"Table \"{table.TableNumber}\" is now {(table.IsActive ? "Active" : "Inactive")}.";
        return RedirectToAction(nameof(Tables));
    }

    // ---- Module 5: Payment Processing (admin-side verification queue) ----

    [HttpGet]
    public async Task<IActionResult> PaymentQueue()
    {
        var pending = await _db.Payments.AsNoTracking()
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

        var payment = await _db.Payments.FindAsync(paymentId);
        if (payment is null) return NotFound();

        await _paymentService.AdminVerifyAsync(paymentId, approve, adminUserId);
        return RedirectToAction(nameof(PaymentQueue));
    }

    // Filtered & Paginated Audit Log view.
    [HttpGet]
    public async Task<IActionResult> AuditLog([FromQuery] AdminAuditLogFilterViewModel filter)
    {
        var query = _db.AuditLogs.AsNoTracking()
            .Include(a => a.PerformedByUser)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.EntityType) && filter.EntityType != "All")
        {
            query = query.Where(a => a.EntityType == filter.EntityType);
        }

        if (!string.IsNullOrWhiteSpace(filter.ActionName))
        {
            var action = filter.ActionName.Trim();
            query = query.Where(a => a.Action.Contains(action));
        }

        if (filter.UserId.HasValue && filter.UserId.Value > 0)
        {
            query = query.Where(a => a.PerformedBy == filter.UserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            if (int.TryParse(search.TrimStart('#'), out var parsedEntityId))
            {
                query = query.Where(a => a.EntityID == parsedEntityId || a.Action.Contains(search));
            }
            else
            {
                query = query.Where(a => a.Action.Contains(search)
                                      || (a.NewValue != null && a.NewValue.Contains(search))
                                      || (a.OldValue != null && a.OldValue.Contains(search))
                                      || (a.PerformedByUser != null && a.PerformedByUser.FullName.Contains(search)));
            }
        }

        if (filter.DateFrom.HasValue)
        {
            var from = filter.DateFrom.Value.Date;
            query = query.Where(a => a.Timestamp >= from);
        }

        if (filter.DateTo.HasValue)
        {
            var toExclusive = filter.DateTo.Value.Date.AddDays(1);
            query = query.Where(a => a.Timestamp < toExclusive);
        }

        var totalCount = await query.CountAsync();
        var pageSize = filter.PageSize > 0 ? filter.PageSize : 30;
        var page = filter.Page > 0 ? filter.Page : 1;

        var logs = await query
            .OrderByDescending(a => a.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        filter.Logs = new PagedResult<AuditLog>
        {
            Items = logs,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };

        filter.EntityTypes = await _db.AuditLogs.AsNoTracking()
            .Select(a => a.EntityType)
            .Distinct()
            .OrderBy(e => e)
            .ToListAsync();

        return View(filter);
    }
}

using Microsoft.EntityFrameworkCore;
using QServe.Data;
using QServe.Models;
using QServe.ViewModels;

namespace QServe.Services;

/// <summary>
/// Module 10: Reporting & Analytics.
///
/// Global convention followed throughout: revenue-bearing figures exclude Cancelled orders,
/// matching the Admin Dashboard's "Revenue Today" stat (Module 8) — if these numbers and that
/// dashboard ever disagree on the same date range, one of them has a bug. The Order Status
/// Report is the one deliberate exception: it needs to show cancellations as a metric, not
/// hide them, so it includes every status.
/// </summary>
public class ReportingService : IReportingService
{
    private readonly ApplicationDbContext _db;

    public ReportingService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<DailySalesSummaryDto> GetDailySalesSummaryAsync(DateTime rangeStart, DateTime rangeEnd)
    {
        // PRD acceptance criterion (10-prd-reporting-analytics.md, §8): "Daily Sales Summary
        // total revenue matches the sum of Received payments for the selected date range."
        // Excluding only Cancelled orders isn't enough — PendingPayment/AwaitingVerification
        // orders have no confirmed money yet and must not be counted as revenue.
        var orders = await _db.Orders
            .Include(o => o.Payments)
            .Where(o => o.CreatedAt >= rangeStart && o.CreatedAt < rangeEnd
                        && o.OrderStatus != OrderStatuses.Cancelled
                        && o.Payments.Any(p => p.PaymentStatus == PaymentStatuses.Received))
            .ToListAsync();

        var revenue = orders.Sum(o => o.TotalAmount);
        var orderCount = orders.Count;

        var modeBreakdown = orders
            .Where(o => o.Payment is not null)
            .GroupBy(o => o.Payment!.PaymentMode)
            .ToDictionary(g => g.Key, g => g.Sum(o => o.TotalAmount));

        return new DailySalesSummaryDto
        {
            RangeStart = rangeStart,
            RangeEnd = rangeEnd,
            Revenue = revenue,
            OrderCount = orderCount,
            AvgOrderValue = orderCount > 0 ? revenue / orderCount : 0,
            RevenueByPaymentMode = modeBreakdown
        };
    }

    public async Task<(List<PopularItemDto> ByQuantity, List<PopularItemDto> ByRevenue)> GetPopularItemsReportAsync(
        DateTime rangeStart, DateTime rangeEnd, int topN = 10)
    {
        // Note: this ranks by actual sales within the selected date range — a different,
        // complementary view from Module 9's GetPopularItemsAsync, which scores by the
        // TotalOrdered/RecentOrdered counters for the "Popular" menu badge. They can
        // legitimately disagree (e.g., an item popular all-time but not this week) —
        // that's not a bug, they're answering different questions.
        var lines = await _db.OrderItems
            .Include(oi => oi.Item)
            .Where(oi => oi.Order!.CreatedAt >= rangeStart && oi.Order.CreatedAt < rangeEnd
                         && oi.Order.OrderStatus != OrderStatuses.Cancelled)
            .ToListAsync();

        var grouped = lines
            .GroupBy(oi => oi.ItemID)
            .Select(g => new PopularItemDto
            {
                ItemName = g.First().Item?.Name ?? "Unknown item",
                QuantitySold = g.Sum(x => x.Quantity),
                Revenue = g.Sum(x => x.Quantity * x.UnitPrice)
            })
            .ToList();

        var byQuantity = grouped.OrderByDescending(x => x.QuantitySold).Take(topN).ToList();
        var byRevenue = grouped.OrderByDescending(x => x.Revenue).Take(topN).ToList();

        return (byQuantity, byRevenue);
    }

    public async Task<List<KitchenThroughputDto>> GetKitchenThroughputAsync(DateTime rangeStart, DateTime rangeEnd)
    {
        var orders = await _db.Orders
            .Include(o => o.OrderItems).ThenInclude(oi => oi.Item)
            .Where(o => o.CreatedAt >= rangeStart && o.CreatedAt < rangeEnd
                        && o.ApprovedAt != null && o.ServedAt != null)
            .ToListAsync();

        // REP-4 defines prep time at the order level (ApprovedAt -> ServedAt), but an order
        // can contain multiple item types. Each distinct item type present in an order gets
        // credited with that order's full duration once — the alternative (splitting the
        // duration across types) has no principled basis since kitchen prep happens in
        // parallel, not sequentially per item.
        var durationsByType = new Dictionary<string, List<double>>();

        foreach (var order in orders)
        {
            var minutes = (order.ServedAt!.Value - order.ApprovedAt!.Value).TotalMinutes;
            var typesInOrder = order.OrderItems
                .Select(oi => oi.Item?.ItemType)
                .Where(t => t is not null)
                .Distinct();

            foreach (var type in typesInOrder)
            {
                if (!durationsByType.TryGetValue(type!, out var list))
                    durationsByType[type!] = list = new List<double>();
                list.Add(minutes);
            }
        }

        return durationsByType
            .Select(kv => new KitchenThroughputDto
            {
                ItemType = kv.Key,
                AvgPrepMinutes = Math.Round(kv.Value.Average(), 1),
                OrdersCompleted = kv.Value.Count
            })
            .OrderBy(x => x.ItemType)
            .ToList();
    }

    public async Task<List<PeakHourDto>> GetPeakHoursAsync(DateTime rangeStart, DateTime rangeEnd)
    {
        var orders = await _db.Orders
            .Where(o => o.CreatedAt >= rangeStart && o.CreatedAt < rangeEnd && o.OrderStatus != OrderStatuses.Cancelled)
            .ToListAsync();

        return orders
            .GroupBy(o => o.CreatedAt.Hour)
            .Select(g => new PeakHourDto { Hour = g.Key, OrderCount = g.Count() })
            .OrderBy(x => x.Hour)
            .ToList();
    }

    public async Task<List<PaymentVerificationLogEntryDto>> GetPaymentVerificationLogAsync(DateTime rangeStart, DateTime rangeEnd)
    {
        // Offline payments only (Online never goes through admin verification) — every one
        // with a VerificationTime, approved or rejected. See the note in
        // GETTING_STARTED.md's Module 10 section: PaymentService.AdminVerifyAsync was updated
        // alongside this report so rejections stamp VerifiedBy/VerificationTime too, not just
        // approvals — otherwise this log would silently miss half the admin's decisions.
        var payments = await _db.Payments
            .Include(p => p.Order).ThenInclude(o => o!.Table)
            .Include(p => p.VerifiedByUser)
            .Where(p => p.PaymentMode != PaymentModes.Online
                        && p.VerificationTime != null
                        && p.VerificationTime >= rangeStart && p.VerificationTime < rangeEnd)
            .OrderByDescending(p => p.VerificationTime)
            .ToListAsync();

        return payments.Select(p => new PaymentVerificationLogEntryDto
        {
            PaymentId = p.PaymentID,
            TableNumber = p.Order?.Table?.TableNumber ?? "",
            PaymentMode = p.PaymentMode,
            Amount = p.Amount,
            Approved = p.PaymentStatus == PaymentStatuses.Received,
            VerifiedByName = p.VerifiedByUser?.FullName ?? "Unknown",
            VerificationTime = p.VerificationTime
        }).ToList();
    }

    public async Task<OrderStatusReportDto> GetOrderStatusReportAsync(DateTime rangeStart, DateTime rangeEnd)
    {
        // Deliberately NOT excluding Cancelled here — this report's whole job is to show
        // cancellations as a metric (REP-6), unlike every revenue-bearing report above.
        var orders = await _db.Orders
            .Where(o => o.CreatedAt >= rangeStart && o.CreatedAt < rangeEnd)
            .ToListAsync();

        var counts = orders.GroupBy(o => o.OrderStatus).ToDictionary(g => g.Key, g => g.Count());
        var total = orders.Count;
        var cancelled = counts.TryGetValue(OrderStatuses.Cancelled, out var c) ? c : 0;

        return new OrderStatusReportDto
        {
            CountsByStatus = counts,
            TotalOrders = total,
            CancellationRatePercent = total > 0 ? Math.Round((double)cancelled / total * 100, 1) : 0
        };
    }

    public async Task<List<TableUtilisationDto>> GetTableUtilisationReportAsync(DateTime rangeStart, DateTime rangeEnd)
    {
        var orders = await _db.Orders
            .Include(o => o.Table)
            .Where(o => o.CreatedAt >= rangeStart && o.CreatedAt < rangeEnd && o.OrderStatus != OrderStatuses.Cancelled)
            .ToListAsync();

        return orders
            .GroupBy(o => o.Table?.TableNumber ?? "Unknown")
            .Select(g => new TableUtilisationDto
            {
                TableNumber = g.Key,
                OrderCount = g.Count(),
                Revenue = g.Sum(o => o.TotalAmount)
            })
            .OrderByDescending(x => x.Revenue)
            .ToList();
    }
    // REP-7: "busiest tables by hour" — required by the Reports Specification table (§3) and
    // REP-7 (§4) alongside the per-table order/revenue ranking above, but previously not built
    // at all. Same Cancelled-exclusion rule as the rest of this report; CreatedAt.Hour is used
    // (not ApprovedAt/ServedAt) since this describes when tables place orders, not kitchen timing.
    public async Task<List<TableHourBucketDto>> GetTableUtilisationByHourAsync(DateTime rangeStart, DateTime rangeEnd)
    {
        var orders = await _db.Orders
            .Include(o => o.Table)
            .Where(o => o.CreatedAt >= rangeStart && o.CreatedAt < rangeEnd && o.OrderStatus != OrderStatuses.Cancelled)
            .ToListAsync();

        return orders
            .GroupBy(o => new { Table = o.Table?.TableNumber ?? "Unknown", Hour = o.CreatedAt.Hour })
            .Select(g => new TableHourBucketDto
            {
                TableNumber = g.Key.Table,
                Hour = g.Key.Hour,
                OrderCount = g.Count()
            })
            .ToList();
    }
}

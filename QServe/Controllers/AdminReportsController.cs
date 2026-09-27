using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QServe.Data;
using QServe.Models;
using QServe.Services;
using QServe.ViewModels;

namespace QServe.Controllers;

/// <summary>
/// Module 10: Reporting & Analytics. REP-8: Admin/Manager only.
/// </summary>
[Authorize(Roles = $"{UserRoles.Admin},{UserRoles.Manager}")]
public class AdminReportsController : Controller
{
    private readonly IReportingService _reports;
    private readonly ApplicationDbContext _db;

    public AdminReportsController(IReportingService reports, ApplicationDbContext db)
    {
        _reports = reports;
        _db = db;
    }

    // REP-1: daily/range sales analytics with historical trend chart
    // PRD Reports Specification (§3) classifies this report's trigger as "Daily / on-demand",
    // and REP-1 (§4) says the default for a daily-trigger report is "today" — not the 7-day
    // default used for weekly-trigger reports (Popular Items, Table Utilisation).
    [HttpGet]
    public async Task<IActionResult> Sales(DateTime? start, DateTime? end)
    {
        var (rangeStart, rangeEnd) = ResolveRange(start, end, defaultDays: 0);
        var report = await _reports.GetDailySalesSummaryAsync(rangeStart, rangeEnd);
        SetRangeViewBag(rangeStart, rangeEnd);

        var rangeOrders = await _db.Orders.AsNoTracking()
            .Where(o => o.CreatedAt >= rangeStart && o.CreatedAt < rangeEnd && o.OrderStatus != OrderStatuses.Cancelled)
            .Select(o => new { o.CreatedAt, o.TotalAmount })
            .ToListAsync();

        var totalDays = Math.Max(1, (int)(rangeEnd.Date - rangeStart.Date).TotalDays);
        var trend = Enumerable.Range(0, totalDays)
            .Select(offset => rangeStart.Date.AddDays(offset))
            .Select(day =>
            {
                var dayOrders = rangeOrders.Where(o => o.CreatedAt.Date == day).ToList();
                return new DailyTrendPoint
                {
                    Date = day,
                    OrderCount = dayOrders.Count,
                    Revenue = dayOrders.Sum(o => o.TotalAmount)
                };
            })
            .ToList();

        ViewBag.Trend = trend;
        return View(report);
    }

    // REP-3: weekly by default.
    [HttpGet]
    public async Task<IActionResult> PopularItems(DateTime? start, DateTime? end)
    {
        var (rangeStart, rangeEnd) = ResolveRange(start, end, defaultDays: 7);
        var (byQuantity, byRevenue) = await _reports.GetPopularItemsReportAsync(rangeStart, rangeEnd);
        SetRangeViewBag(rangeStart, rangeEnd);
        ViewBag.ByRevenue = byRevenue;
        return View(byQuantity);
    }

    // REP-4: daily by default (throughput is most useful compared day-to-day).
    [HttpGet]
    public async Task<IActionResult> Throughput(DateTime? start, DateTime? end)
    {
        var (rangeStart, rangeEnd) = ResolveRange(start, end, defaultDays: 1);
        var throughput = await _reports.GetKitchenThroughputAsync(rangeStart, rangeEnd);
        var peakHours = await _reports.GetPeakHoursAsync(rangeStart, rangeEnd);
        SetRangeViewBag(rangeStart, rangeEnd);
        ViewBag.PeakHours = peakHours;
        return View(throughput);
    }

    // REP-5: on-demand, daily by default with pagination support.
    [HttpGet]
    public async Task<IActionResult> PaymentLog(DateTime? start, DateTime? end, int page = 1, int pageSize = 10)
    {
        pageSize = Math.Clamp(pageSize > 0 ? pageSize : 10, 1, 100);
        page = Math.Max(1, page > 0 ? page : 1);

        var (rangeStart, rangeEnd) = ResolveRange(start, end, defaultDays: 1);
        var allLogs = await _reports.GetPaymentVerificationLogAsync(rangeStart, rangeEnd);
        SetRangeViewBag(rangeStart, rangeEnd);

        var totalCount = allLogs.Count;
        var pagedItems = allLogs.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        var pagedResult = new PagedResult<PaymentVerificationLogEntryDto>
        {
            Items = pagedItems,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };

        return View(pagedResult);
    }

    // REP-6: on-demand, daily by default.
    [HttpGet]
    public async Task<IActionResult> OrderStatus(DateTime? start, DateTime? end)
    {
        var (rangeStart, rangeEnd) = ResolveRange(start, end, defaultDays: 1);
        var report = await _reports.GetOrderStatusReportAsync(rangeStart, rangeEnd);
        SetRangeViewBag(rangeStart, rangeEnd);
        return View(report);
    }

    // REP-7: weekly by default.
    [HttpGet]
    public async Task<IActionResult> TableUtilisation(DateTime? start, DateTime? end)
    {
        var (rangeStart, rangeEnd) = ResolveRange(start, end, defaultDays: 7);
        var report = await _reports.GetTableUtilisationReportAsync(rangeStart, rangeEnd);
        var byHour = await _reports.GetTableUtilisationByHourAsync(rangeStart, rangeEnd);
        SetRangeViewBag(rangeStart, rangeEnd);
        ViewBag.ByHour = byHour;
        return View(report);
    }

    // REP-1: every report supports a date-range filter; this resolves the query-string dates
    // (or a sensible default) into a half-open [start, end) range in one place so all six
    // actions treat "today" and "last 7 days" the same way.
    //
    // Gap fix: previously took whatever start/end came in on the query string at face value —
    // a hand-edited URL with start > end, or a typo'd year, would silently produce an empty or
    // absurdly large report with no error shown. Now guards both.
    private static (DateTime Start, DateTime End) ResolveRange(DateTime? start, DateTime? end, int defaultDays)
    {
        const int maxRangeDays = 366; // a year — generous for any of these reports, cheap to raise later

        var inclusiveEnd = end ?? DateTime.UtcNow.Date;
        var inclusiveStart = start ?? inclusiveEnd.AddDays(-defaultDays);

        // A reversed range (end typed before start) almost certainly means the two got swapped,
        // not that the person wants zero days of data — swap them back rather than erroring.
        if (inclusiveStart > inclusiveEnd)
            (inclusiveStart, inclusiveEnd) = (inclusiveEnd, inclusiveStart);

        // Clamp an oversized range rather than rejecting it outright — a typo'd year (e.g.
        // "2020" instead of "2026") still returns something useful (the last year of data)
        // instead of an error page.
        if ((inclusiveEnd - inclusiveStart).TotalDays > maxRangeDays)
            inclusiveStart = inclusiveEnd.AddDays(-maxRangeDays);

        return (inclusiveStart, inclusiveEnd.AddDays(1)); // exclusive upper bound
    }

    private void SetRangeViewBag(DateTime start, DateTime end)
    {
        ViewBag.RangeStart = start;
        ViewBag.RangeEnd = end.AddDays(-1); // show the inclusive end date in the date picker
    }
}

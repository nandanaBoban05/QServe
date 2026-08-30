using QServe.Models;

namespace QServe.ViewModels;

/// <summary>
/// View model for the operational real-time Admin Command Center dashboard.
/// Prioritizes live restaurant and kitchen operations.
/// </summary>
public class AdminDashboardStats
{
    // ---- Top-level KPI Metrics ----
    public decimal TodayRevenue { get; set; }
    public decimal YesterdayRevenue { get; set; }
    public int TodayOrderCount { get; set; }
    public int ActiveOrderCount { get; set; }
    public int PendingActionCount { get; set; }
    public int PendingVerificationCount { get; set; }

    // ---- Kitchen Activity Live Status Counts ----
    public int NewOrdersCount { get; set; }
    public int PreparingOrdersCount { get; set; }
    public int ReadyOrdersCount { get; set; }
    public int CompletedOrdersCount { get; set; }

    // ---- Live Active Kitchen Order Tickets ----
    public List<Order> ActiveKitchenOrders { get; set; } = new();

    // ---- Recent Order Stream (5-10 latest) ----
    public List<Order> RecentOrders { get; set; } = new();

    // ---- Quick Operational & Sales Summary ----
    public decimal TodayOnlineRevenue { get; set; }
    public decimal TodayOfflineRevenue { get; set; }
    public int TodayCompletedOrderCount { get; set; }
    public int TodayCancelledOrderCount { get; set; }
    public decimal AverageOrderValue => TodayOrderCount > 0 ? (TodayRevenue / TodayOrderCount) : 0m;
    public int ActiveTableCount { get; set; }
    public int MenuItemCount { get; set; }
    public int ActiveStaffCount { get; set; }

    // ---- Recent Activity Feed (Audit Log) ----
    public List<RecentActivityItem> RecentActivity { get; set; } = new();

    // ---- Historical Trend (accessible via Analytics / Reports) ----
    public List<DailyTrendPoint> WeekTrend { get; set; } = new();
}

public class DailyTrendPoint
{
    public DateTime Date { get; set; }
    public int OrderCount { get; set; }
    public decimal Revenue { get; set; }
}

public class RecentActivityItem
{
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int EntityID { get; set; }
    public string? PerformedByName { get; set; }
    public DateTime Timestamp { get; set; }
}

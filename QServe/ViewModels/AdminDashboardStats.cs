namespace QServe.ViewModels;

/// <summary>
/// View-only shape for the Admin dashboard landing page (ADM-7) — not persisted, not a
/// broadcast payload (that's Hubs/RealtimeDtos.cs). Non-persisted, view-only classes like this
/// belong here rather than in Models/ (entities) or Hubs/ (SignalR payloads).
/// </summary>
public class AdminDashboardStats
{
    public int TodayOrderCount { get; set; }
    public decimal TodayRevenue { get; set; }
    public int PendingVerificationCount { get; set; }

    // Added for the improved dashboard pass: operational counts that give a real system feel
    // rather than just the three payment-focused figures above.
    public int ActiveOrderCount { get; set; }
    public int ActiveTableCount { get; set; }
    public int MenuItemCount { get; set; }
    public int ActiveStaffCount { get; set; }

    // 7-day trend for the dashboard chart — oldest first.
    public List<DailyTrendPoint> WeekTrend { get; set; } = new();

    public List<RecentActivityItem> RecentActivity { get; set; } = new();
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

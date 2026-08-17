namespace QServe.ViewModels;

/// <summary>
/// Module 10: Reporting & Analytics. One DTO per report, following the DailySalesSummary
/// shape specified in the original proposal — a dedicated report DTO per report, not a
/// generic "report result" bag.
/// </summary>
public class DailySalesSummaryDto
{
    public DateTime RangeStart { get; set; }
    public DateTime RangeEnd { get; set; }
    public decimal Revenue { get; set; }
    public int OrderCount { get; set; }
    public decimal AvgOrderValue { get; set; }
    public Dictionary<string, decimal> RevenueByPaymentMode { get; set; } = new();
}

public class PopularItemDto
{
    public string ItemName { get; set; } = string.Empty;
    public int QuantitySold { get; set; }
    public decimal Revenue { get; set; }
}

public class KitchenThroughputDto
{
    public string ItemType { get; set; } = string.Empty;
    public double AvgPrepMinutes { get; set; }
    public int OrdersCompleted { get; set; }
}

public class PeakHourDto
{
    public int Hour { get; set; }
    public int OrderCount { get; set; }
}

public class PaymentVerificationLogEntryDto
{
    public int PaymentId { get; set; }
    public string TableNumber { get; set; } = string.Empty;
    public string PaymentMode { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool Approved { get; set; }
    public string VerifiedByName { get; set; } = string.Empty;
    public DateTime? VerificationTime { get; set; }
}

public class OrderStatusReportDto
{
    public Dictionary<string, int> CountsByStatus { get; set; } = new();
    public int TotalOrders { get; set; }
    public double CancellationRatePercent { get; set; }
}

public class TableUtilisationDto
{
    public string TableNumber { get; set; } = string.Empty;
    public int OrderCount { get; set; }
    public decimal Revenue { get; set; }
}

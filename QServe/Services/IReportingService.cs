using QServe.ViewModels;

namespace QServe.Services;

public interface IReportingService
{
    /// <summary>REP-1/REP-2: revenue, order count, avg order value, split by payment mode.</summary>
    Task<DailySalesSummaryDto> GetDailySalesSummaryAsync(DateTime rangeStart, DateTime rangeEnd);

    /// <summary>REP-3: top items by quantity sold and by revenue, independently ranked.</summary>
    Task<(List<PopularItemDto> ByQuantity, List<PopularItemDto> ByRevenue)> GetPopularItemsReportAsync(
        DateTime rangeStart, DateTime rangeEnd, int topN = 10);

    /// <summary>REP-4: avg prep time (ApprovedAt -> ServedAt) grouped by item type, plus peak order hours.</summary>
    Task<List<KitchenThroughputDto>> GetKitchenThroughputAsync(DateTime rangeStart, DateTime rangeEnd);

    Task<List<PeakHourDto>> GetPeakHoursAsync(DateTime rangeStart, DateTime rangeEnd);

    /// <summary>REP-5: every offline payment decision (approved or rejected) with who and when.</summary>
    Task<List<PaymentVerificationLogEntryDto>> GetPaymentVerificationLogAsync(DateTime rangeStart, DateTime rangeEnd);

    /// <summary>REP-6: order counts by status plus overall cancellation rate.</summary>
    Task<OrderStatusReportDto> GetOrderStatusReportAsync(DateTime rangeStart, DateTime rangeEnd);

    /// <summary>REP-7: orders and revenue per table, ranked.</summary>
    Task<List<TableUtilisationDto>> GetTableUtilisationReportAsync(DateTime rangeStart, DateTime rangeEnd);

    /// <summary>REP-7: "busiest tables by hour" breakdown — order count per table, per hour-of-day.</summary>
    Task<List<TableHourBucketDto>> GetTableUtilisationByHourAsync(DateTime rangeStart, DateTime rangeEnd);
}

using QServe.Models;

namespace QServe.Services;

public interface IRecommendationService
{
    /// <summary>
    /// REC-3/REC-4: pure scoring formula — Score = (TotalOrdered * 0.7) + (RecentOrdered * 0.3).
    /// No DB access, fully unit-testable in isolation, same spirit as Module 6's OrderClassifier.
    /// </summary>
    decimal ComputeScore(int totalOrdered, int recentOrdered);

    /// <summary>
    /// REC-4/REC-5: top-N available items by score, for the "Popular" badge on the customer menu.
    /// </summary>
    Task<List<MenuItem>> GetPopularItemsAsync(int topN = 5);

    /// <summary>
    /// REC-1: called on order approval — increments TotalOrdered for every item in the order
    /// by the quantity ordered. Hooked into PaymentService alongside the AI classifier call.
    /// </summary>
    Task OnOrderApprovedAsync(int orderId);

    /// <summary>
    /// REC-2/REC-6: recomputes RecentOrdered for every menu item from the trailing 7 days of
    /// OrderItems (excluding Cancelled orders), rather than incrementing it live — see the
    /// notes in GETTING_STARTED.md on why a live counter would drift.
    /// </summary>
    Task RecomputeRecentOrderedAsync();
}

using QServe.Services;
using Xunit;

namespace QServe.Tests;

/// <summary>
/// ComputeScore is deliberately a plain function with no DB dependency (same reasoning as
/// OrderClassifierTests) — these map to the acceptance criteria in
/// docs/prd/09-prd-recommendation-engine.md.
/// </summary>
public class RecommendationServiceTests
{
    private readonly RecommendationService _service = new(db: null!); // ComputeScore doesn't touch the DB

    [Fact]
    public void Score_WeightsTotalOrderedAt70Percent_AndRecentOrderedAt30Percent()
    {
        var score = _service.ComputeScore(totalOrdered: 100, recentOrdered: 10);
        Assert.Equal(73m, score); // (100 * 0.7) + (10 * 0.3) = 70 + 3
    }

    [Fact]
    public void HighAllTimeVolume_ZeroRecent_CanBeOutscoredByATrendingItem()
    {
        // The PRD's acceptance criteria: an item with high all-time orders but zero recent
        // orders can score lower than a currently-trending item, once recent volume is large
        // enough — the 30% weight on RecentOrdered has to be able to overcome a TotalOrdered
        // gap, not just nudge the score slightly.
        var oldFavorite = _service.ComputeScore(totalOrdered: 50, recentOrdered: 0);     // 35
        var trending = _service.ComputeScore(totalOrdered: 5, recentOrdered: 200);        // 63.5

        Assert.True(trending > oldFavorite);
    }

    [Fact]
    public void ZeroOrders_ScoresZero()
    {
        Assert.Equal(0m, _service.ComputeScore(0, 0));
    }

    [Fact]
    public void Score_IsPure_SameInputsAlwaysProduceSameOutput()
    {
        var first = _service.ComputeScore(20, 5);
        var second = _service.ComputeScore(20, 5);
        Assert.Equal(first, second);
    }
}

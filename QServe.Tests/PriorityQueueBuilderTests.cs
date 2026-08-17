using QServe.Models;
using QServe.Services;
using Xunit;

namespace QServe.Tests;

public class PriorityQueueBuilderTests
{
    private readonly PriorityQueueBuilder _builder = new();

    private static Order MakeOrder(int id, string type, DateTime approvedAt) => new()
    {
        OrderID = id,
        OrderType = type,
        OrderStatus = OrderStatuses.Approved,
        ApprovedAt = approvedAt,
        TableID = 1
    };

    [Fact]
    public void QuickOrder_AlwaysComesBeforeHeavyOrder_EvenIfApprovedLater()
    {
        var baseTime = DateTime.UtcNow;

        // A Heavy order approved first, then a Quick order approved later —
        // the Module 6 PRD's core promise is that Quick still jumps the queue.
        var heavyFirst = MakeOrder(1, OrderTypes.Heavy, baseTime);
        var quickLater = MakeOrder(2, OrderTypes.Quick, baseTime.AddMinutes(5));

        var result = _builder.Build(new[] { heavyFirst, quickLater });

        Assert.Equal(2, result[0].OrderID); // Quick order is first despite arriving later
        Assert.Equal(1, result[1].OrderID);
    }

    [Fact]
    public void SameTier_SortsByApprovedAt_FIFO()
    {
        var baseTime = DateTime.UtcNow;
        var second = MakeOrder(1, OrderTypes.Regular, baseTime.AddMinutes(2));
        var first = MakeOrder(2, OrderTypes.Regular, baseTime);

        var result = _builder.Build(new[] { second, first });

        Assert.Equal(2, result[0].OrderID); // approved earlier, so first in queue
        Assert.Equal(1, result[1].OrderID);
    }

    [Fact]
    public void MixedTiers_SortIntoQuickThenRegularThenHeavy()
    {
        var baseTime = DateTime.UtcNow;
        var heavy = MakeOrder(1, OrderTypes.Heavy, baseTime);
        var regular = MakeOrder(2, OrderTypes.Regular, baseTime);
        var quick = MakeOrder(3, OrderTypes.Quick, baseTime);

        // Deliberately passed in "wrong" order to prove the builder does the sorting.
        var result = _builder.Build(new[] { heavy, regular, quick });

        Assert.Equal(new[] { 3, 2, 1 }, result.Select(o => o.OrderID));
    }
}

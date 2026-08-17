using QServe.Models;

namespace QServe.Services;

public class PriorityQueueBuilder : IPriorityQueueBuilder
{
    private static readonly Dictionary<string, int> TierRank = new()
    {
        [OrderTypes.Quick] = 0,
        [OrderTypes.Regular] = 1,
        [OrderTypes.Heavy] = 2
    };

    public List<Order> Build(IEnumerable<Order> approvedOrders) =>
        approvedOrders
            .OrderBy(o => TierRank.TryGetValue(o.OrderType, out var rank) ? rank : int.MaxValue)
            .ThenBy(o => o.ApprovedAt ?? o.CreatedAt)
            .ToList();
}

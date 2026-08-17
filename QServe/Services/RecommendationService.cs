using Microsoft.EntityFrameworkCore;
using QServe.Data;
using QServe.Models;

namespace QServe.Services;

/// <summary>
/// Module 9: Recommendation Engine. Simple scoring over order-volume counters — no ML, no
/// per-customer personalisation (there's no customer identity to personalise against; see
/// Module 4's scope). Explicitly out of scope per the proposal — don't be tempted to add
/// collaborative filtering here without revisiting the PRD first.
/// </summary>
public class RecommendationService : IRecommendationService
{
    private readonly ApplicationDbContext _db;

    public RecommendationService(ApplicationDbContext db)
    {
        _db = db;
    }

    public decimal ComputeScore(int totalOrdered, int recentOrdered) =>
        (totalOrdered * 0.7m) + (recentOrdered * 0.3m);

    public async Task<List<MenuItem>> GetPopularItemsAsync(int topN = 5)
    {
        // REC-5: only available items are eligible — no promoting something customers can't
        // actually order. Scoring happens in memory (not translated to SQL) since ComputeScore
        // needs to stay a plain C# function callers can unit test without EF Core in the loop;
        // the menu table is small enough that this is not a real performance concern.
        var availableItems = await _db.MenuItems.Where(i => i.IsAvailable).ToListAsync();

        return availableItems
            .OrderByDescending(i => ComputeScore(i.TotalOrdered, i.RecentOrdered))
            .Take(topN)
            .ToList();
    }

    public async Task OnOrderApprovedAsync(int orderId)
    {
        var orderItems = await _db.OrderItems.Where(oi => oi.OrderID == orderId).ToListAsync();
        if (orderItems.Count == 0) return;

        var itemIds = orderItems.Select(oi => oi.ItemID).ToList();
        var menuItems = await _db.MenuItems.Where(i => itemIds.Contains(i.ItemID)).ToDictionaryAsync(i => i.ItemID);

        foreach (var line in orderItems)
        {
            if (menuItems.TryGetValue(line.ItemID, out var item))
                item.TotalOrdered += line.Quantity;
        }

        await _db.SaveChangesAsync();
    }

    public async Task RecomputeRecentOrderedAsync()
    {
        var cutoff = DateTime.UtcNow.AddDays(-7);

        // REC-6: derive RecentOrdered fresh from the last 7 days each time, rather than
        // incrementing a running counter and trying to decay it — a derived value can't drift
        // from reality the way an incremented-then-decremented counter eventually can.
        var recentTotals = await _db.OrderItems
            .Where(oi => oi.Order!.CreatedAt >= cutoff && oi.Order.OrderStatus != OrderStatuses.Cancelled)
            .GroupBy(oi => oi.ItemID)
            .Select(g => new { ItemID = g.Key, Total = g.Sum(oi => oi.Quantity) })
            .ToListAsync();

        var totalsByItem = recentTotals.ToDictionary(x => x.ItemID, x => x.Total);

        var allItems = await _db.MenuItems.ToListAsync();
        foreach (var item in allItems)
        {
            item.RecentOrdered = totalsByItem.TryGetValue(item.ItemID, out var total) ? total : 0;
        }

        await _db.SaveChangesAsync();
    }
}

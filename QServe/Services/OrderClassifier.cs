using Microsoft.EntityFrameworkCore;
using QServe.Data;
using QServe.Models;

namespace QServe.Services;

/// <summary>
/// Module 6: AI Order Prioritisation.
///
/// This is a rule-based classifier, not a trained ML model — see the Module 6 PRD's note on
/// why "AI/Expert Systems" in the proposal means deterministic business rules here, not
/// machine learning. Keep it that way: the value of this module is that it's simple,
/// predictable, and fully covered by unit tests with no external dependencies.
/// </summary>
public class OrderClassifier : IOrderClassifier
{
    private readonly ApplicationDbContext _db;

    public OrderClassifier(ApplicationDbContext db)
    {
        _db = db;
    }

    public string Classify(IEnumerable<string> itemTypesInOrder)
    {
        var heavyCount = itemTypesInOrder.Count(t => t is ItemTypes.Cooked or ItemTypes.Dessert);

        // AI-2/AI-3/AI-4
        if (heavyCount == 0) return OrderTypes.Quick;
        if (heavyCount <= 2) return OrderTypes.Regular;
        return OrderTypes.Heavy;
    }

    public async Task<string> ClassifyAndSaveAsync(int orderId)
    {
        var order = await _db.Orders.Include(o => o.OrderItems).ThenInclude(oi => oi.Item)
            .FirstOrDefaultAsync(o => o.OrderID == orderId)
            ?? throw new InvalidOperationException($"Order {orderId} not found.");

        var itemTypes = order.OrderItems.Select(oi => oi.Item?.ItemType ?? ItemTypes.Quick);
        order.OrderType = Classify(itemTypes);

        await _db.SaveChangesAsync();
        return order.OrderType;
    }
}

namespace QServe.Services;

public interface IOrderClassifier
{
    /// <summary>
    /// Pure classification logic (AI-2 through AI-4) — no DB access, fully unit-testable.
    /// Cooked/Dessert items count toward the "heavy" tally; everything else (Beverage, Quick)
    /// doesn't hold up the kitchen queue.
    /// </summary>
    string Classify(IEnumerable<string> itemTypesInOrder);

    /// <summary>
    /// AI-1/AI-6: loads an order's items, classifies it, and persists OrderType.
    /// Called by Payment Processing at the moment an order transitions to Approved.
    /// </summary>
    Task<string> ClassifyAndSaveAsync(int orderId);
}

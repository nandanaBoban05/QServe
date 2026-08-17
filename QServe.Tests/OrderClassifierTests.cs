using QServe.Models;
using QServe.Services;
using Xunit;

namespace QServe.Tests;

/// <summary>
/// These map directly to the acceptance criteria in docs/prd/06-prd-ai-prioritisation.md.
/// If you change the classification thresholds, update the PRD and these tests together —
/// they're meant to stay in lockstep.
/// </summary>
public class OrderClassifierTests
{
    private readonly OrderClassifier _classifier = new(db: null!); // Classify() doesn't touch the DB

    [Fact]
    public void TwoBeveragesOnly_ClassifiesAsQuick()
    {
        var result = _classifier.Classify(new[] { ItemTypes.Beverage, ItemTypes.Beverage });
        Assert.Equal(OrderTypes.Quick, result);
    }

    [Fact]
    public void OneBeverageOneCookedDish_ClassifiesAsRegular()
    {
        var result = _classifier.Classify(new[] { ItemTypes.Beverage, ItemTypes.Cooked });
        Assert.Equal(OrderTypes.Regular, result);
    }

    [Fact]
    public void ThreeCookedDishes_ClassifiesAsHeavy()
    {
        var result = _classifier.Classify(new[] { ItemTypes.Cooked, ItemTypes.Cooked, ItemTypes.Cooked });
        Assert.Equal(OrderTypes.Heavy, result);
    }

    [Fact]
    public void EmptyOrder_ClassifiesAsQuick()
    {
        // Edge case not explicitly in the PRD's acceptance criteria but implied by AI-2
        // (zero heavy items -> Quick) — worth pinning down explicitly.
        var result = _classifier.Classify(Array.Empty<string>());
        Assert.Equal(OrderTypes.Quick, result);
    }

    [Fact]
    public void TwoCookedOneDessert_ClassifiesAsHeavy()
    {
        // Boundary check: exactly 3 heavy items should tip from Regular into Heavy (AI-4: >= 3).
        var result = _classifier.Classify(new[] { ItemTypes.Cooked, ItemTypes.Cooked, ItemTypes.Dessert });
        Assert.Equal(OrderTypes.Heavy, result);
    }

    [Fact]
    public void ExactlyTwoHeavyItems_ClassifiesAsRegular()
    {
        // Boundary check on the other side: exactly 2 heavy items is still Regular (AI-3: 1 or 2).
        var result = _classifier.Classify(new[] { ItemTypes.Cooked, ItemTypes.Dessert });
        Assert.Equal(OrderTypes.Regular, result);
    }

    [Theory]
    [InlineData(ItemTypes.Beverage, ItemTypes.Beverage, ItemTypes.Beverage)] // still Quick regardless of count, if none are Cooked/Dessert
    public void OnlyNonHeavyTypes_AlwaysQuick_RegardlessOfQuantity(params string[] itemTypes)
    {
        var result = _classifier.Classify(itemTypes);
        Assert.Equal(OrderTypes.Quick, result);
    }

    [Fact]
    public void SameOrderContents_AlwaysProduceSameClassification()
    {
        // Module 6 PRD acceptance criteria: classification is a pure function of order
        // contents. Calling it twice with identical input must never differ.
        var items = new[] { ItemTypes.Cooked, ItemTypes.Beverage };
        var first = _classifier.Classify(items);
        var second = _classifier.Classify(items);
        Assert.Equal(first, second);
    }
}

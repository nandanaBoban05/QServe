using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QServe.Models;

public class MenuItem
{
    [Key]
    public int ItemID { get; set; }

    public int CategoryID { get; set; }
    public MenuCategory? Category { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Column(TypeName = "decimal(10,2)")]
    public decimal Price { get; set; }

    public int PrepTimeMinutes { get; set; } = 10;

    // Valid values enforced via CHECK constraint: Beverage, Cooked, Dessert, Quick
    // Used by the AI classifier (Module 6) — Cooked/Dessert count toward Heavy/Regular tiers
    [Required, MaxLength(15)]
    public string ItemType { get; set; } = ItemTypes.Quick;

    public bool IsAvailable { get; set; } = true;

    // Used by the Recommendation Engine (Module 9)
    public int TotalOrdered { get; set; } = 0;
    public int RecentOrdered { get; set; } = 0;

    [MaxLength(300)]
    public string? ImageUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
}

public static class ItemTypes
{
    public const string Beverage = "Beverage";
    public const string Cooked = "Cooked";
    public const string Dessert = "Dessert";
    public const string Quick = "Quick";
}

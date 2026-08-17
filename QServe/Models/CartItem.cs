using System.ComponentModel.DataAnnotations;

namespace QServe.Models;

/// <summary>
/// In-memory cart line item, held in session (no persistent customer identity exists —
/// see Module 4 PRD, "Out of Scope: customer accounts of any kind").
/// Matches the CartItem data structure specified in the original proposal.
/// </summary>
public class CartItem
{
    [Key]
    public int ItemID { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string? Customization { get; set; }
    public string ItemType { get; set; } = string.Empty;

    public decimal LineTotal => Quantity * UnitPrice;
}

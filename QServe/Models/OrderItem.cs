using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QServe.Models;

public class OrderItem
{
    [Key]
    public int OrderItemID { get; set; }

    public int OrderID { get; set; }
    public Order? Order { get; set; }

    public int ItemID { get; set; }
    public MenuItem? Item { get; set; }

    public int Quantity { get; set; }

    // Price snapshot at order time — do not recompute from current MenuItem.Price
    [Column(TypeName = "decimal(10,2)")]
    public decimal UnitPrice { get; set; }

    [System.ComponentModel.DataAnnotations.MaxLength(300)]
    public string? Customization { get; set; }

    [NotMapped]
    public decimal LineTotal => Quantity * UnitPrice;
}

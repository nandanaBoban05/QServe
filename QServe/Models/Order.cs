using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QServe.Models;

public class Order
{
    [Key]
    public int OrderID { get; set; }

    public int TableID { get; set; }
    public RestaurantTable? Table { get; set; }

    // Valid values enforced via CHECK constraint — see OrderStatuses below.
    // Lifecycle: PendingPayment -> AwaitingVerification -> Approved -> Preparing -> Ready -> Served
    //                                                    \-> Cancelled
    [Required, MaxLength(30)]
    public string OrderStatus { get; set; } = OrderStatuses.PendingPayment;

    // Set by the AI Prioritisation module (Module 6) on approval
    [Required, MaxLength(10)]
    public string OrderType { get; set; } = OrderTypes.Regular;

    [Column(TypeName = "decimal(10,2)")]
    public decimal TotalAmount { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedAt { get; set; }
    public DateTime? ServedAt { get; set; }

    // Navigation
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    public Payment? Payment { get; set; }
}

public static class OrderStatuses
{
    public const string PendingPayment = "PendingPayment";
    public const string AwaitingVerification = "AwaitingVerification";
    public const string Approved = "Approved";
    public const string Preparing = "Preparing";
    public const string Ready = "Ready";
    public const string Served = "Served";
    public const string Cancelled = "Cancelled";
}

public static class OrderTypes
{
    public const string Quick = "Quick";
    public const string Regular = "Regular";
    public const string Heavy = "Heavy";
}

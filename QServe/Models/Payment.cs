using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QServe.Models;

public class Payment
{
    [Key]
    public int PaymentID { get; set; }

    // One payment per order — unique constraint set in OnModelCreating
    public int OrderID { get; set; }
    public Order? Order { get; set; }

    // Valid values: Online, Cash, Card
    [Required, MaxLength(10)]
    public string PaymentMode { get; set; } = PaymentModes.Cash;

    // Valid values: Pending, Processing, Received, Failed, Refunded
    [Required, MaxLength(15)]
    public string PaymentStatus { get; set; } = PaymentStatuses.Pending;

    [Column(TypeName = "decimal(10,2)")]
    public decimal Amount { get; set; }

    [MaxLength(100)]
    public string? RazorpayOrderID { get; set; }

    [MaxLength(100)]
    public string? RazorpayPaymentID { get; set; }

    // Admin who verified an offline (Cash/Card) payment
    public int? VerifiedBy { get; set; }
    public User? VerifiedByUser { get; set; }

    public DateTime? VerificationTime { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class PaymentModes
{
    public const string Online = "Online";
    public const string Cash = "Cash";
    public const string Card = "Card";
}

public static class PaymentStatuses
{
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Received = "Received";
    public const string Failed = "Failed";
    public const string Refunded = "Refunded";
}

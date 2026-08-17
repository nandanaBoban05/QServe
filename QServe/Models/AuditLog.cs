using System.ComponentModel.DataAnnotations;

namespace QServe.Models;

public class AuditLog
{
    [Key]
    public long LogID { get; set; }

    [Required, MaxLength(50)]
    public string EntityType { get; set; } = string.Empty; // e.g. "Orders", "Payments", "MenuItems"

    public int EntityID { get; set; }

    [Required, MaxLength(100)]
    public string Action { get; set; } = string.Empty; // e.g. "StatusChanged", "PaymentVerified"

    public string? OldValue { get; set; } // JSON
    public string? NewValue { get; set; } // JSON

    public int? PerformedBy { get; set; }
    public User? PerformedByUser { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

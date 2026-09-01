using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace QServe.Models;

public class User : IdentityUser<int>
{
    // Backwards compatibility alias for Primary Key Id
    [NotMapped]
    public int UserID
    {
        get => Id;
        set => Id = value;
    }

    [Required, MaxLength(100)]
    public string FullName { get; set; } = string.Empty;

    // Valid values enforced via CHECK constraint in OnModelCreating: Admin, Kitchen, Manager
    [Required, MaxLength(20)]
    public string Role { get; set; } = UserRoles.Kitchen;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Added for self-service Forgot Password. PasswordResetTokenHash stores a SHA-256 hash of
    // the raw token. A null hash means no reset is currently in flight for this account.
    public string? PasswordResetTokenHash { get; set; }
    public DateTime? PasswordResetTokenExpiry { get; set; }

    // Navigation
    public ICollection<Payment> VerifiedPayments { get; set; } = new List<Payment>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}

public static class UserRoles
{
    public const string Admin = "Admin";
    public const string Kitchen = "Kitchen";
    public const string Manager = "Manager";
}

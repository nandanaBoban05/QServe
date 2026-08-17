using System.ComponentModel.DataAnnotations;

namespace QServe.Models;

public class User
{
    [Key]
    public int UserID { get; set; }

    [Required, MaxLength(100)]
    public string FullName { get; set; } = string.Empty;

    [Required, MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    [Required, MaxLength(512)]
    public string PasswordHash { get; set; } = string.Empty;

    // Valid values enforced via CHECK constraint in OnModelCreating: Admin, Kitchen, Manager
    [Required, MaxLength(20)]
    public string Role { get; set; } = "Kitchen";

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Added for Module 2 (Authentication) — brute-force lockout tracking.
    // Not part of the original Module 1 schema; safe additive columns.
    public int AccessFailedCount { get; set; } = 0;
    public DateTime? LockoutEnd { get; set; }

    // Added for self-service Forgot Password. PasswordResetTokenHash stores a SHA-256 hash of
    // the raw token, never the token itself — same principle as PasswordHash never storing a
    // plaintext password. A null hash means no reset is currently in flight for this account.
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

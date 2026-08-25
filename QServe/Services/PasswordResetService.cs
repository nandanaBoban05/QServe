using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using QServe.Data;
using QServe.Models;

namespace QServe.Services;

public class PasswordResetService : IPasswordResetService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(1);

    private readonly ApplicationDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly IEmailTemplateService _templateService;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;

    public PasswordResetService(
        ApplicationDbContext db, 
        IEmailSender emailSender, 
        IEmailTemplateService templateService,
        IConfiguration config, 
        IWebHostEnvironment env)
    {
        _db = db;
        _emailSender = emailSender;
        _templateService = templateService;
        _config = config;
        _env = env;
    }

    public async Task<string?> RequestResetAsync(string email)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email && u.IsActive);

        // Enumeration safety: the RETURN VALUE never differs based on whether the account
        // exists in Production (always null there), and the caller shows one generic message
        // regardless. We do skip the token/email work entirely for a non-match, which is a
        // minor timing signal — accepted trade-off for a staff-only internal tool, not a
        // public consumer product where that timing difference would matter more.
        if (user is null)
            return null;

        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var rawToken = Convert.ToBase64String(tokenBytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();

        user.PasswordResetTokenHash = tokenHash;
        user.PasswordResetTokenExpiry = DateTime.UtcNow.Add(TokenLifetime);
        await _db.SaveChangesAsync();

        var baseUrl = _config["App:BaseUrl"]?.TrimEnd('/')
            ?? throw new InvalidOperationException("App:BaseUrl is not configured.");
        var resetLink = $"{baseUrl}/Account/ResetPassword?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(rawToken)}";

        // Render HTML template
        var htmlBody = await _templateService.RenderPasswordResetTemplateAsync(
            user.FullName ?? "User",
            resetLink);

        await _emailSender.SendAsync(new EmailMessage(
            ToEmail: email,
            Subject: "Reset your QServe password",
            Body: htmlBody));

        return _env.IsDevelopment() ? resetLink : null;
    }

    public async Task<bool> ResetPasswordAsync(string email, string token, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            return false;

        if (string.IsNullOrWhiteSpace(token))
            return false;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email && u.IsActive);
        if (user?.PasswordResetTokenHash is null || user.PasswordResetTokenExpiry is null)
            return false;

        if (user.PasswordResetTokenExpiry.Value < DateTime.UtcNow)
            return false; // expired — caller should prompt for a fresh request, not retry this token

        var suppliedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        var tokenValid = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(suppliedHash),
            Encoding.UTF8.GetBytes(user.PasswordResetTokenHash));

        if (!tokenValid)
            return false;

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        // Single-use: clear the token immediately so this same link can't be replayed.
        user.PasswordResetTokenHash = null;
        user.PasswordResetTokenExpiry = null;
        // A fresh password shouldn't stay locked out behind old failed login attempts.
        user.AccessFailedCount = 0;
        user.LockoutEnd = null;

        _db.AuditLogs.Add(new AuditLog
        {
            EntityType = "Users",
            EntityID = user.UserID,
            Action = "PasswordResetSelfService",
            PerformedBy = null, // no admin involved — the user reset their own password
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        return true;
    }
}

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QServe.Data;
using QServe.Models;

namespace QServe.Services;

public class PasswordResetService : IPasswordResetService
{
    private readonly UserManager<User> _userManager;
    private readonly ApplicationDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly IEmailTemplateService _templateService;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<PasswordResetService> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PasswordResetService(
        UserManager<User> userManager,
        ApplicationDbContext db,
        IEmailSender emailSender,
        IEmailTemplateService templateService,
        IConfiguration config,
        IWebHostEnvironment env,
        ILogger<PasswordResetService> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        _db = db;
        _emailSender = emailSender;
        _templateService = templateService;
        _config = config;
        _env = env;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<string?> RequestResetAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        var user = await _userManager.FindByEmailAsync(email);

        // Enumeration safety: the RETURN VALUE never differs based on whether the account exists
        if (user is null || !user.IsActive)
            return null;

        // Invalidate all previous reset tokens immediately so only the latest link works
        await _userManager.UpdateSecurityStampAsync(user);

        var rawToken = await _userManager.GeneratePasswordResetTokenAsync(user);

        _db.AuditLogs.Add(new AuditLog
        {
            EntityType = "Users",
            EntityID = user.Id,
            Action = "PasswordResetRequested",
            PerformedBy = null,
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        var baseUrl = ResolveBaseUrl();
        var resetLink = $"{baseUrl}/Account/ResetPassword?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(rawToken)}";

        try
        {
            var htmlBody = await _templateService.RenderPasswordResetTemplateAsync(
                user.FullName ?? "Staff Member",
                resetLink);

            await _emailSender.SendAsync(new EmailMessage(
                ToEmail: email,
                Subject: "Reset your QServe password",
                Body: htmlBody));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send password reset email to {Email}", email);
        }

        return _env.IsDevelopment() ? resetLink : null;
    }

    public async Task<bool> ResetPasswordAsync(string email, string token, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            return false;

        var user = await _userManager.FindByEmailAsync(email);
        if (user is null || !user.IsActive)
            return false;

        var result = await _userManager.ResetPasswordAsync(user, token, newPassword);
        if (!result.Succeeded)
            return false;

        await _userManager.ResetAccessFailedCountAsync(user);
        await _userManager.SetLockoutEndDateAsync(user, null);

        _db.AuditLogs.Add(new AuditLog
        {
            EntityType = "Users",
            EntityID = user.Id,
            Action = "PasswordResetSelfService",
            PerformedBy = null,
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        return true;
    }

    private string ResolveBaseUrl()
    {
        var configured = _config["App:BaseUrl"];
        if (!string.IsNullOrWhiteSpace(configured) && !configured.Contains("localhost", StringComparison.OrdinalIgnoreCase))
            return configured.TrimEnd('/');

        var request = _httpContextAccessor.HttpContext?.Request;
        if (request is not null)
            return $"{request.Scheme}://{request.Host}";

        throw new InvalidOperationException("App:BaseUrl is not configured and no active HTTP request is available to derive it from.");
    }
}
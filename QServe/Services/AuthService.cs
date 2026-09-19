using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QServe.Data;
using QServe.Models;

namespace QServe.Services;

public class AuthService : IAuthService
{
    private readonly UserManager<User> _userManager;
    private readonly SignInManager<User> _signInManager;
    private readonly ApplicationDbContext _db;

    public AuthService(
        UserManager<User> userManager,
        SignInManager<User> signInManager,
        ApplicationDbContext db)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _db = db;
    }

    public async Task<LoginOutcome> ValidateLoginAsync(string email, string password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return new LoginOutcome(LoginResult.InvalidCredentials, null, null, null);

        var user = await _userManager.FindByEmailAsync(email);

        // Per PRD AUTH-2: don't reveal whether the email exists — generic result either way.
        if (user is null)
            return new LoginOutcome(LoginResult.InvalidCredentials, null, null, null);

        if (!user.IsActive)
            return new LoginOutcome(LoginResult.AccountInactive, null, null, null);

        if (await _userManager.IsLockedOutAsync(user))
            return new LoginOutcome(LoginResult.AccountLocked, null, null, null);

        var result = await _signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            await _userManager.ResetAccessFailedCountAsync(user);

            _db.AuditLogs.Add(new AuditLog
            {
                EntityType = "Users",
                EntityID = user.Id,
                Action = "LoginSuccess",
                PerformedBy = user.Id,
                Timestamp = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();

            return new LoginOutcome(LoginResult.Success, user.Id, user.Role, user.FullName);
        }

        var isLockedNow = await _userManager.IsLockedOutAsync(user);

        _db.AuditLogs.Add(new AuditLog
        {
            EntityType = "Users",
            EntityID = user.Id,
            Action = isLockedNow ? "AccountLocked" : "LoginFailed",
            PerformedBy = null,
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        return isLockedNow
            ? new LoginOutcome(LoginResult.AccountLocked, null, null, null)
            : new LoginOutcome(LoginResult.InvalidCredentials, null, null, null);
    }
}

using Microsoft.EntityFrameworkCore;
using QServe.Data;

namespace QServe.Services;

public class AuthService : IAuthService
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly ApplicationDbContext _db;

    public AuthService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<LoginOutcome> ValidateLoginAsync(string email, string password)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);

        // Per PRD AUTH-2: don't reveal whether the email exists — generic result either way.
        if (user is null)
            return new LoginOutcome(LoginResult.InvalidCredentials, null, null, null);

        if (!user.IsActive)
            return new LoginOutcome(LoginResult.AccountInactive, null, null, null);

        if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTime.UtcNow)
            return new LoginOutcome(LoginResult.AccountLocked, null, null, null);

        bool passwordMatches = BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);

        if (!passwordMatches)
        {
            user.AccessFailedCount++;
            if (user.AccessFailedCount >= MaxFailedAttempts)
            {
                user.LockoutEnd = DateTime.UtcNow.Add(LockoutDuration);
            }
            await _db.SaveChangesAsync();

            return user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTime.UtcNow
                ? new LoginOutcome(LoginResult.AccountLocked, null, null, null)
                : new LoginOutcome(LoginResult.InvalidCredentials, null, null, null);
        }

        // Success — reset failed-attempt counter
        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
        await _db.SaveChangesAsync();

        return new LoginOutcome(LoginResult.Success, user.UserID, user.Role, user.FullName);
    }
}

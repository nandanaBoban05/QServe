namespace QServe.Services;

public enum LoginResult
{
    Success,
    InvalidCredentials,
    AccountLocked,
    AccountInactive
}

public record LoginOutcome(LoginResult Result, int? UserId, string? Role, string? FullName);

public interface IAuthService
{
    /// <summary>
    /// Validates credentials, enforces lockout policy, and returns the outcome.
    /// Does not sign the user in — the caller (AccountController) issues the auth cookie
    /// on Success so this service stays free of HTTP concerns and is unit-testable.
    /// </summary>
    Task<LoginOutcome> ValidateLoginAsync(string email, string password);
}

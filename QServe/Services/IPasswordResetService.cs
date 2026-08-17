namespace QServe.Services;

public interface IPasswordResetService
{
    /// <summary>
    /// Requests a password reset for the given email. Safe to call for any string, whether or
    /// not it matches an account — the caller should show the same generic confirmation either
    /// way (AUTH-2-style: don't reveal whether an email exists).
    ///
    /// Returns the raw reset link ONLY when running in the Development environment AND the
    /// email matched an active account — that's what lets the Forgot Password page show the
    /// link directly for testing, since no real email sender is configured by default (see
    /// DevEmailSender). In every other case (Production, or an unmatched email) this returns
    /// null, and the caller must not distinguish those two cases in what it shows the user.
    /// </summary>
    Task<string?> RequestResetAsync(string email);

    /// <summary>
    /// Completes a reset: validates the token (hash match + not expired) against the given
    /// email, and if valid, sets the new password and clears the token and any lockout.
    /// Returns false for any failure — invalid email, invalid/expired token, or a password
    /// that fails the minimum-length check — without distinguishing which, so a failed attempt
    /// doesn't leak which part was wrong.
    /// </summary>
    Task<bool> ResetPasswordAsync(string email, string token, string newPassword);
}

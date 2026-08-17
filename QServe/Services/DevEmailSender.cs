namespace QServe.Services;

/// <summary>
/// Default IEmailSender: logs the email instead of sending it. This is what makes Forgot
/// Password fully functional in this scaffold without needing real email infrastructure
/// configured first — but it means no email actually leaves the server. PasswordResetService
/// additionally surfaces the raw reset link directly on the Forgot Password page, and ONLY in
/// the Development environment, so the feature is testable end-to-end without this sender
/// being replaced. Do not ship this to production — swap the registration in Program.cs for a
/// real sender; IEmailSender is the only interface anything else in the app depends on.
/// </summary>
public class DevEmailSender : IEmailSender
{
    private readonly ILogger<DevEmailSender> _logger;

    public DevEmailSender(ILogger<DevEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(EmailMessage message)
    {
        _logger.LogInformation("=== DEV EMAIL (not actually sent) ===\nTo: {To}\nSubject: {Subject}\n{Body}\n=====================================",
            message.ToEmail, message.Subject, message.Body);
        return Task.CompletedTask;
    }
}

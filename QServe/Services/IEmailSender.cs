namespace QServe.Services;

public record EmailMessage(string ToEmail, string Subject, string Body);

/// <summary>
/// New feature (Forgot Password): abstracts email sending so the reset flow's logic doesn't
/// care how a message actually gets delivered. See DevEmailSender for the default
/// implementation shipped with this scaffold — swap the DI registration in Program.cs for a
/// real SMTP/SendGrid/etc. sender before this goes anywhere near production.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message);
}

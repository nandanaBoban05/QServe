using System.Net;
using System.Net.Mail;

namespace QServe.Services;

/// <summary>
/// Gmail SMTP implementation of IEmailSender. This sends actual emails through Gmail's SMTP server.
/// Requires Gmail app-specific password (not the main account password) for security.
/// Configuration is read from appsettings.json under "EmailConfig" section.
/// </summary>
public class GmailEmailSender : IEmailSender
{
    private readonly ILogger<GmailEmailSender> _logger;
    private readonly IConfiguration _config;

    public GmailEmailSender(ILogger<GmailEmailSender> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    /// <summary>
    /// Determines if the body content is HTML based on common HTML markers.
    /// </summary>
    private static bool IsHtmlContent(string body)
    {
        return body.Contains('<') && (body.Contains("<!DOCTYPE") || body.Contains("<html") || body.Contains("<body"));
    }

    public async Task SendAsync(EmailMessage message)
    {
        try
        {
            // Read Gmail SMTP configuration from appsettings.json
            var smtpHost = _config["EmailConfig:SmtpHost"] ?? throw new InvalidOperationException("EmailConfig:SmtpHost is not configured.");
            var smtpPort = int.Parse(_config["EmailConfig:SmtpPort"] ?? "587");
            var senderEmail = _config["EmailConfig:SenderEmail"] ?? throw new InvalidOperationException("EmailConfig:SenderEmail is not configured.");
            var senderPassword = _config["EmailConfig:SenderPassword"] ?? throw new InvalidOperationException("EmailConfig:SenderPassword is not configured.");
            var senderDisplayName = _config["EmailConfig:SenderDisplayName"] ?? "QServe";

            using (var smtpClient = new SmtpClient(smtpHost, smtpPort))
            {
                // Gmail requires TLS encryption on port 587
                smtpClient.EnableSsl = true;
                smtpClient.Credentials = new NetworkCredential(senderEmail, senderPassword);
                smtpClient.Timeout = 10000; // 10 second timeout

                using (var mailMessage = new MailMessage())
                {
                    mailMessage.From = new MailAddress(senderEmail, senderDisplayName);
                    mailMessage.To.Add(new MailAddress(message.ToEmail));
                    mailMessage.Subject = message.Subject;
                    mailMessage.Body = message.Body;
                    // Detect if body is HTML based on content
                    mailMessage.IsBodyHtml = IsHtmlContent(message.Body);

                    await smtpClient.SendMailAsync(mailMessage);
                    _logger.LogInformation("Email sent successfully to {ToEmail} with subject '{Subject}'", message.ToEmail, message.Subject);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {ToEmail}. Subject: '{Subject}'", message.ToEmail, message.Subject);
            throw; // Re-throw to allow caller to handle
        }
    }
}

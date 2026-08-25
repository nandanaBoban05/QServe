namespace QServe.Services;

/// <summary>
/// Service for rendering and managing email templates.
/// </summary>
public interface IEmailTemplateService
{
    /// <summary>
    /// Render an email template with the provided variables.
    /// </summary>
    Task<string> RenderPasswordResetTemplateAsync(string fullName, string resetLink);
}

public class EmailTemplateService : IEmailTemplateService
{
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;

    public EmailTemplateService(IWebHostEnvironment env, IConfiguration config)
    {
        _env = env;
        _config = config;
    }

    public async Task<string> RenderPasswordResetTemplateAsync(string fullName, string resetLink)
    {
        var templatePath = Path.Combine(_env.ContentRootPath, "Views","Account", "PasswordResetTemplate.html");

        if (!File.Exists(templatePath))
            throw new FileNotFoundException($"Email template not found: {templatePath}");

        var content = await File.ReadAllTextAsync(templatePath);

        var baseUrl = _config["App:BaseUrl"]?.TrimEnd('/') ?? "https://qserve.local";
        // Replace template variables
        content = content.Replace("{{FullName}}", HtmlEncode(fullName), StringComparison.OrdinalIgnoreCase);
        content = content.Replace("{{ResetLink}}", resetLink, StringComparison.OrdinalIgnoreCase);
        content = content.Replace("{{AppBaseUrl}}", baseUrl, StringComparison.OrdinalIgnoreCase);
        content = content.Replace("{{CurrentYear}}", DateTime.Now.Year.ToString(), StringComparison.OrdinalIgnoreCase);

        return content;
    }

    private static string HtmlEncode(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return System.Web.HttpUtility.HtmlEncode(text);
    }
}

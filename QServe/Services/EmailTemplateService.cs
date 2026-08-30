using Microsoft.AspNetCore.Http;

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
    private readonly IHttpContextAccessor _httpContextAccessor;

    public EmailTemplateService(IWebHostEnvironment env, IConfiguration config, IHttpContextAccessor httpContextAccessor)
    {
        _env = env;
        _config = config;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<string> RenderPasswordResetTemplateAsync(string fullName, string resetLink)
    {
        var templatePath = Path.Combine(_env.ContentRootPath, "Views", "Account", "PasswordResetTemplate.html");

        if (!File.Exists(templatePath))
            throw new FileNotFoundException($"Email template not found: {templatePath}");

        var content = await File.ReadAllTextAsync(templatePath);

        var baseUrl = ResolveBaseUrl();
        // Replace template variables
        content = content.Replace("{{FullName}}", HtmlEncode(fullName), StringComparison.OrdinalIgnoreCase);
        content = content.Replace("{{ResetLink}}", resetLink, StringComparison.OrdinalIgnoreCase);
        content = content.Replace("{{AppBaseUrl}}", baseUrl, StringComparison.OrdinalIgnoreCase);
        content = content.Replace("{{CurrentYear}}", DateTime.Now.Year.ToString(), StringComparison.OrdinalIgnoreCase);

        return content;
    }

    private string ResolveBaseUrl()
    {
        var configured = _config["App:BaseUrl"];
        if (!string.IsNullOrWhiteSpace(configured) && !configured.Contains("localhost", StringComparison.OrdinalIgnoreCase))
            return configured.TrimEnd('/');

        var request = _httpContextAccessor.HttpContext?.Request;
        if (request is not null)
            return $"{request.Scheme}://{request.Host}";

        return "https://qserve.local"; // last-resort default, matches original fallback behavior
    }

    private static string HtmlEncode(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return System.Web.HttpUtility.HtmlEncode(text);
    }
}
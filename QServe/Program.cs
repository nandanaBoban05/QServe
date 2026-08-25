using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using QServe.Data;
using QServe.Hubs;
using QServe.Services;

var builder = WebApplication.CreateBuilder(args);

// ---- Module 1: Database & Core Domain ----
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// MVC + Razor Views
builder.Services.AddControllersWithViews();

// ---- Module 2: Authentication & RBAC ----
// Custom cookie auth backed directly by the Users table (see Services/AuthService.cs)
// rather than full ASP.NET Core Identity, since the schema is bespoke to this project.
builder.Services.AddScoped<IAuthService, AuthService>();

// Forgot Password (self-service). Uses GmailEmailSender when EmailConfig has real credentials;
// falls back to DevEmailSender (log-only) when placeholders are still in config.
builder.Services.AddScoped<GmailEmailSender>();
builder.Services.AddScoped<DevEmailSender>();
builder.Services.AddScoped<IEmailSender>(sp =>
{
    var password = sp.GetRequiredService<IConfiguration>()["EmailConfig:SenderPassword"];
    if (string.IsNullOrWhiteSpace(password) || password.StartsWith("REPLACE", StringComparison.OrdinalIgnoreCase))
        return sp.GetRequiredService<DevEmailSender>();
    return sp.GetRequiredService<GmailEmailSender>();
});
builder.Services.AddScoped<IEmailTemplateService, EmailTemplateService>();
builder.Services.AddScoped<IPasswordResetService, PasswordResetService>();
builder.Services.AddScoped<IQrCodeService, QrCodeService>();

// ---- Module 5: Payment Processing ----
builder.Services.AddHttpClient();
builder.Services.AddScoped<IPaymentService, PaymentService>();

// ---- Module 6: AI Order Prioritisation ----
builder.Services.AddScoped<IOrderClassifier, OrderClassifier>();
builder.Services.AddScoped<IPriorityQueueBuilder, PriorityQueueBuilder>();

// ---- Module 9: Recommendation Engine ----
builder.Services.AddScoped<IRecommendationService, RecommendationService>();
builder.Services.AddHostedService<RecentOrderedRecalculationService>();

// ---- Module 10: Reporting & Analytics ----
builder.Services.AddScoped<IReportingService, ReportingService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();

// ---- Module 4: Customer Ordering ----
// Session-based cart storage — there's no persistent customer identity (no login), so the
// cart lives server-side keyed by a session cookie, scoped per table.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax; // Lax, not Strict — customers arrive via a scanned QR link
    options.IdleTimeout = TimeSpan.FromHours(2);
});

// ---- Module 7: Kitchen Display System ----
builder.Services.AddSignalR();
builder.Services.AddScoped<IRealtimeNotifier, RealtimeNotifier>();

builder.Services.AddHealthChecks();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    // Fail-fast configuration validation
    var baseUrl = app.Configuration["App:BaseUrl"];
    if (string.IsNullOrWhiteSpace(baseUrl) || baseUrl.Contains("localhost"))
        throw new InvalidOperationException("App:BaseUrl is missing or invalid for Production. Set this to the public URL.");

    var qrSecret = app.Configuration["QrCode:SigningSecret"];
    if (string.IsNullOrWhiteSpace(qrSecret) || qrSecret.StartsWith("REPLACE"))
        throw new InvalidOperationException("QrCode:SigningSecret is missing or using a placeholder.");

    var dbConn = app.Configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrWhiteSpace(dbConn))
        throw new InvalidOperationException("ConnectionStrings:DefaultConnection is missing.");

    if (string.IsNullOrWhiteSpace(app.Configuration["Razorpay:KeyId"]) || 
        string.IsNullOrWhiteSpace(app.Configuration["Razorpay:KeySecret"]) || 
        string.IsNullOrWhiteSpace(app.Configuration["Razorpay:WebhookSecret"]))
        throw new InvalidOperationException("Razorpay configuration (KeyId, KeySecret, WebhookSecret) is incomplete.");

    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
    });

    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.MapHub<KitchenHub>("/hubs/kitchen");
app.MapHealthChecks("/health");

app.Run();

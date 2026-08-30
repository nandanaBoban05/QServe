using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
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

// Needed so services (QrCodeService, PasswordResetService, EmailTemplateService) can read
// the current request's scheme/host to build URLs when App:BaseUrl isn't explicitly set —
// this is what lets QR/reset/email links automatically match a Dev Tunnel URL.
builder.Services.AddHttpContextAccessor();

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
        options.Events = new CookieAuthenticationEvents
        {
            OnValidatePrincipal = async context =>
            {
                var userIdRaw = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (int.TryParse(userIdRaw, out var userId))
                {
                    var db = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
                    var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserID == userId);
                    if (user == null || !user.IsActive)
                    {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    }
                }
            }
        };
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

// Must run in Development too — ASP.NET Core Dev Tunnels route traffic through a relay
// that sets X-Forwarded-Proto/X-Forwarded-Host, and code that reads Request.Scheme/
// Request.Host (QrCodeService, AdminController.Tables, etc.) depends on this being
// processed BEFORE those reads happen. XForwardedHost is added (the original block only
// forwarded For+Proto) because the tunnel's public hostname differs from Kestrel's local
// binding. KnownNetworks/KnownProxies are cleared because they default to loopback-only,
// and the Dev Tunnel relay isn't a loopback address.
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor
        | ForwardedHeaders.XForwardedProto
        | ForwardedHeaders.XForwardedHost
};
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

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

// Automatically ensure RejectionReason column and Payments index are up to date on SQL Server
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    try
    {
        if (db.Database.IsSqlServer())
        {
            db.Database.ExecuteSqlRaw(@"
                IF NOT EXISTS (
                    SELECT 1 FROM sys.columns 
                    WHERE Name = N'RejectionReason' 
                    AND Object_ID = Object_ID(N'Payments')
                )
                BEGIN
                    ALTER TABLE Payments ADD RejectionReason NVARCHAR(255) NULL;
                END;

                IF EXISTS (
                    SELECT 1 FROM sys.indexes 
                    WHERE name = N'IX_Payments_OrderID' 
                    AND object_id = OBJECT_ID(N'Payments') 
                    AND is_unique = 1
                )
                BEGIN
                    DROP INDEX IX_Payments_OrderID ON Payments;
                    CREATE INDEX IX_Payments_OrderID ON Payments(OrderID);
                END;
            ");
        }
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetService<ILogger<Program>>();
        logger?.LogWarning(ex, "Could not run automated schema update for Payments.RejectionReason.");
    }
}

app.Run();
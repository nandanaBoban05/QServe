using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using QServe.Controllers;
using QServe.Data;
using QServe.Models;
using QServe.Services;
using Xunit;

namespace QServe.Tests;

public class AccountSecurityTests
{
    private readonly ApplicationDbContext _db;
    private readonly AuthService _authService;
    private readonly PasswordResetService _passwordResetService;
    private readonly Mock<IEmailSender> _emailSenderMock = new();
    private readonly Mock<IEmailTemplateService> _templateServiceMock = new();
    private readonly Mock<IConfiguration> _configMock = new();
    private readonly Mock<IWebHostEnvironment> _envMock = new();
    private readonly Mock<ILogger<PasswordResetService>> _loggerMock = new();

    public AccountSecurityTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);

        _configMock.Setup(c => c["App:BaseUrl"]).Returns("https://qserve.example.com");
        _envMock.Setup(e => e.EnvironmentName).Returns("Development");
        _templateServiceMock.Setup(t => t.RenderPasswordResetTemplateAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("<html>Password Reset</html>");

        _authService = new AuthService(_db);
        _passwordResetService = new PasswordResetService(
            _db,
            _emailSenderMock.Object,
            _templateServiceMock.Object,
            _configMock.Object,
            _envMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task ValidateLogin_WithValidCredentials_ReturnsSuccess()
    {
        var user = new User
        {
            FullName = "Admin User",
            Email = "admin@qserve.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("StrongPass123!"),
            Role = UserRoles.Admin,
            IsActive = true
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var result = await _authService.ValidateLoginAsync("admin@qserve.local", "StrongPass123!");

        Assert.Equal(LoginResult.Success, result.Result);
        Assert.Equal(user.UserID, result.UserId);
        Assert.Equal(UserRoles.Admin, result.Role);
    }

    [Fact]
    public async Task ValidateLogin_WithInvalidPassword_ReturnsInvalidCredentials_AndIncrementsFailedCount()
    {
        var user = new User
        {
            FullName = "Admin User",
            Email = "admin@qserve.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("StrongPass123!"),
            Role = UserRoles.Admin,
            IsActive = true,
            AccessFailedCount = 0
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var result = await _authService.ValidateLoginAsync("admin@qserve.local", "WrongPassword");

        Assert.Equal(LoginResult.InvalidCredentials, result.Result);
        var dbUser = await _db.Users.FindAsync(user.UserID);
        Assert.Equal(1, dbUser!.AccessFailedCount);
    }

    [Fact]
    public async Task ValidateLogin_WithFiveFailedAttempts_LocksAccount()
    {
        var user = new User
        {
            FullName = "Admin User",
            Email = "admin@qserve.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("StrongPass123!"),
            Role = UserRoles.Admin,
            IsActive = true,
            AccessFailedCount = 4 // 5th will trigger lockout
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var result = await _authService.ValidateLoginAsync("admin@qserve.local", "WrongPassword");

        Assert.Equal(LoginResult.AccountLocked, result.Result);
        var dbUser = await _db.Users.FindAsync(user.UserID);
        Assert.NotNull(dbUser!.LockoutEnd);
        Assert.True(dbUser.LockoutEnd > DateTime.UtcNow);
    }

    [Fact]
    public async Task ValidateLogin_WithNonExistentEmail_ReturnsInvalidCredentials_WithoutLeakingExistence()
    {
        var result = await _authService.ValidateLoginAsync("nobody@qserve.local", "SomePassword123!");

        Assert.Equal(LoginResult.InvalidCredentials, result.Result);
        Assert.Null(result.UserId);
    }

    [Fact]
    public async Task ValidateLogin_WithInactiveAccount_ReturnsAccountInactive()
    {
        var user = new User
        {
            FullName = "Deactivated Staff",
            Email = "inactive@qserve.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("StrongPass123!"),
            Role = UserRoles.Kitchen,
            IsActive = false
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var result = await _authService.ValidateLoginAsync("inactive@qserve.local", "StrongPass123!");

        Assert.Equal(LoginResult.AccountInactive, result.Result);
    }

    [Fact]
    public async Task RequestReset_WithValidEmail_GeneratesTokenAndDispatchesEmail()
    {
        var user = new User
        {
            FullName = "Chef Bob",
            Email = "chef@qserve.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("OldPass123!"),
            Role = UserRoles.Kitchen,
            IsActive = true
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var devLink = await _passwordResetService.RequestResetAsync("chef@qserve.local");

        Assert.NotNull(devLink);
        var dbUser = await _db.Users.FindAsync(user.UserID);
        Assert.NotNull(dbUser!.PasswordResetTokenHash);
        Assert.NotNull(dbUser.PasswordResetTokenExpiry);

        _emailSenderMock.Verify(e => e.SendAsync(It.Is<EmailMessage>(m => m.ToEmail == "chef@qserve.local")), Times.Once);
    }

    [Fact]
    public async Task RequestReset_WithUnknownEmail_ReturnsNull_WithoutLeakingAccountAbsence()
    {
        var result = await _passwordResetService.RequestResetAsync("nonexistent@qserve.local");

        Assert.Null(result);
        _emailSenderMock.Verify(e => e.SendAsync(It.IsAny<EmailMessage>()), Times.Never);
    }

    [Fact]
    public async Task ResetPassword_WithValidToken_UpdatesPasswordHash_AndClearsToken()
    {
        var user = new User
        {
            FullName = "Chef Bob",
            Email = "chef@qserve.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("OldPass123!"),
            Role = UserRoles.Kitchen,
            IsActive = true
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var devLink = await _passwordResetService.RequestResetAsync("chef@qserve.local");
        var token = devLink!.Split("token=")[1];

        var resetSuccess = await _passwordResetService.ResetPasswordAsync("chef@qserve.local", token, "BrandNewPassword123!");

        Assert.True(resetSuccess);
        var dbUser = await _db.Users.FindAsync(user.UserID);
        Assert.Null(dbUser!.PasswordResetTokenHash);
        Assert.Null(dbUser.PasswordResetTokenExpiry);
        Assert.True(BCrypt.Net.BCrypt.Verify("BrandNewPassword123!", dbUser.PasswordHash));
    }

    [Fact]
    public async Task ResetPassword_WithReusedToken_Fails()
    {
        var user = new User
        {
            FullName = "Chef Bob",
            Email = "chef@qserve.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("OldPass123!"),
            Role = UserRoles.Kitchen,
            IsActive = true
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var devLink = await _passwordResetService.RequestResetAsync("chef@qserve.local");
        var token = devLink!.Split("token=")[1];

        // 1st reset
        await _passwordResetService.ResetPasswordAsync("chef@qserve.local", token, "BrandNewPassword123!");

        // 2nd replay attempt
        var secondAttempt = await _passwordResetService.ResetPasswordAsync("chef@qserve.local", token, "AnotherPassword123!");

        Assert.False(secondAttempt);
    }

    [Fact]
    public async Task StaffManagement_CannotDeactivateOwnAccount()
    {
        var admin = new User
        {
            FullName = "Super Admin",
            Email = "admin@qserve.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("StrongPass123!"),
            Role = UserRoles.Admin,
            IsActive = true
        };
        _db.Users.Add(admin);
        await _db.SaveChangesAsync();

        var controller = new AdminStaffController(_db);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, admin.UserID.ToString()),
            new(ClaimTypes.Role, UserRoles.Admin)
        };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")) }
        };
        controller.TempData = new TempDataDictionary(controller.HttpContext, Mock.Of<ITempDataProvider>());

        var result = await controller.ToggleActive(admin.UserID) as RedirectToActionResult;

        Assert.NotNull(result);
        var dbAdmin = await _db.Users.FindAsync(admin.UserID);
        Assert.True(dbAdmin!.IsActive); // Account remained active
    }

    [Fact]
    public async Task StaffManagement_CannotDeactivateLastActiveAdmin()
    {
        var admin1 = new User
        {
            FullName = "Admin One",
            Email = "admin1@qserve.local",
            PasswordHash = "hash1",
            Role = UserRoles.Admin,
            IsActive = true
        };
        var admin2 = new User
        {
            FullName = "Admin Two",
            Email = "admin2@qserve.local",
            PasswordHash = "hash2",
            Role = UserRoles.Admin,
            IsActive = true
        };
        _db.Users.AddRange(admin1, admin2);
        await _db.SaveChangesAsync();

        var controller = new AdminStaffController(_db);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, admin1.UserID.ToString()),
            new(ClaimTypes.Role, UserRoles.Admin)
        };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")) }
        };
        controller.TempData = new TempDataDictionary(controller.HttpContext, Mock.Of<ITempDataProvider>());

        // Deactivate admin2 (leaves 1 admin)
        await controller.ToggleActive(admin2.UserID);
        var dbAdmin2 = await _db.Users.FindAsync(admin2.UserID);
        Assert.False(dbAdmin2!.IsActive);

        // Now attempt to deactivate admin1 (which is the last active admin)
        // Login as another user context to test the last-admin check
        var claimsStaff = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "999"),
            new(ClaimTypes.Role, UserRoles.Admin)
        };
        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claimsStaff, "TestAuth"));

        await controller.ToggleActive(admin1.UserID);
        var dbAdmin1 = await _db.Users.FindAsync(admin1.UserID);
        Assert.True(dbAdmin1!.IsActive); // Was not deactivated because it is the last admin
    }
}

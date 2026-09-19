using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    private readonly UserManager<User> _userManager;
    private readonly SignInManager<User> _signInManager;
    private readonly AuthService _authService;
    private readonly PasswordResetService _passwordResetService;
    private readonly Mock<IEmailSender> _emailSenderMock = new();
    private readonly Mock<IEmailTemplateService> _templateServiceMock = new();
    private readonly Mock<IConfiguration> _configMock = new();
    private readonly Mock<IWebHostEnvironment> _envMock = new();
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock = new();
    private readonly Mock<ILogger<PasswordResetService>> _loggerMock = new();

    public AccountSecurityTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);

        var (userManager, signInManager) = CreateIdentityManagers(_db);
        _userManager = userManager;
        _signInManager = signInManager;

        _configMock.Setup(c => c["App:BaseUrl"]).Returns("https://qserve.example.com");
        _envMock.Setup(e => e.EnvironmentName).Returns("Development");
        _templateServiceMock.Setup(t => t.RenderPasswordResetTemplateAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("<html>Password Reset</html>");

        _authService = new AuthService(_userManager, _signInManager, _db);
        _passwordResetService = new PasswordResetService(
                _userManager,
                _db,
                _emailSenderMock.Object,
                _templateServiceMock.Object,
                _configMock.Object,
                _envMock.Object,
                _loggerMock.Object,
                _httpContextAccessorMock.Object);
    }

    private static (UserManager<User> UserManager, SignInManager<User> SignInManager) CreateIdentityManagers(ApplicationDbContext db)
    {
        var userStore = new UserStore<User, IdentityRole<int>, ApplicationDbContext, int>(db);
        var passwordHasher = new PasswordHasher<User>();
        var userOptions = new OptionsWrapper<IdentityOptions>(new IdentityOptions
        {
            Lockout = new LockoutOptions
            {
                DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15),
                MaxFailedAccessAttempts = 5,
                AllowedForNewUsers = true
            }
        });

        var userManager = new UserManager<User>(
            userStore,
            userOptions,
            passwordHasher,
            new IUserValidator<User>[] { new UserValidator<User>() },
            new IPasswordValidator<User>[] { new PasswordValidator<User>() },
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            (IServiceProvider)null!,
            Mock.Of<ILogger<UserManager<User>>>());

        userManager.RegisterTokenProvider(TokenOptions.DefaultProvider, new EmailTokenProvider<User>());

        var roleStore = new RoleStore<IdentityRole<int>, ApplicationDbContext, int>(db);
        var roleManager = new RoleManager<IdentityRole<int>>(roleStore, null!, null!, null!, null!);

        var contextAccessor = new Mock<IHttpContextAccessor>();
        var claimsFactory = new UserClaimsPrincipalFactory<User, IdentityRole<int>>(userManager, roleManager, userOptions);

        var signInManager = new SignInManager<User>(
            userManager,
            contextAccessor.Object,
            claimsFactory,
            userOptions,
            Mock.Of<ILogger<SignInManager<User>>>(),
            null!,
            null!);

        return (userManager, signInManager);
    }

    [Fact]
    public async Task ValidateLogin_WithValidCredentials_ReturnsSuccess()
    {
        var user = new User
        {
            FullName = "Admin User",
            Email = "admin@qserve.local",
            UserName = "admin@qserve.local",
            Role = UserRoles.Admin,
            IsActive = true
        };
        await _userManager.CreateAsync(user, "StrongPass123!");

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
            UserName = "admin@qserve.local",
            Role = UserRoles.Admin,
            IsActive = true
        };
        await _userManager.CreateAsync(user, "StrongPass123!");

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
            UserName = "admin@qserve.local",
            Role = UserRoles.Admin,
            IsActive = true
        };
        await _userManager.CreateAsync(user, "StrongPass123!");
        await _userManager.SetLockoutEnabledAsync(user, true);

        for (int i = 0; i < 4; i++)
        {
            await _userManager.AccessFailedAsync(user);
        }

        var result = await _authService.ValidateLoginAsync("admin@qserve.local", "WrongPassword");

        Assert.Equal(LoginResult.AccountLocked, result.Result);
        var dbUser = await _db.Users.FindAsync(user.UserID);
        Assert.NotNull(dbUser!.LockoutEnd);
        Assert.True(dbUser.LockoutEnd > DateTimeOffset.UtcNow);
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
            UserName = "inactive@qserve.local",
            Role = UserRoles.Kitchen,
            IsActive = false
        };
        await _userManager.CreateAsync(user, "StrongPass123!");

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
            UserName = "chef@qserve.local",
            Role = UserRoles.Kitchen,
            IsActive = true
        };
        await _userManager.CreateAsync(user, "OldPass123!");

        var devLink = await _passwordResetService.RequestResetAsync("chef@qserve.local");

        Assert.NotNull(devLink);
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
            UserName = "chef@qserve.local",
            Role = UserRoles.Kitchen,
            IsActive = true
        };
        await _userManager.CreateAsync(user, "OldPass123!");

        var devLink = await _passwordResetService.RequestResetAsync("chef@qserve.local");
        var token = devLink!.Split("token=")[1];

        var resetSuccess = await _passwordResetService.ResetPasswordAsync("chef@qserve.local", Uri.UnescapeDataString(token), "BrandNewPassword123!");

        Assert.True(resetSuccess);
        var dbUser = await _db.Users.FindAsync(user.UserID);
        Assert.True(await _userManager.CheckPasswordAsync(dbUser!, "BrandNewPassword123!"));
    }

    [Fact]
    public async Task ResetPassword_WithReusedToken_Fails()
    {
        var user = new User
        {
            FullName = "Chef Bob",
            Email = "chef@qserve.local",
            UserName = "chef@qserve.local",
            Role = UserRoles.Kitchen,
            IsActive = true
        };
        await _userManager.CreateAsync(user, "OldPass123!");

        var devLink = await _passwordResetService.RequestResetAsync("chef@qserve.local");
        var token = devLink!.Split("token=")[1];
        var unescapedToken = Uri.UnescapeDataString(token);

        // 1st reset
        await _passwordResetService.ResetPasswordAsync("chef@qserve.local", unescapedToken, "BrandNewPassword123!");

        // 2nd replay attempt
        var secondAttempt = await _passwordResetService.ResetPasswordAsync("chef@qserve.local", unescapedToken, "AnotherPassword123!");

        Assert.False(secondAttempt);
    }

    [Fact]
    public async Task RequestReset_WhenNewLinkRequested_InvalidatesPreviousToken_AndOnlyLatestTokenWorks()
    {
        var user = new User
        {
            FullName = "Chef Bob",
            Email = "chef@qserve.local",
            UserName = "chef@qserve.local",
            Role = UserRoles.Kitchen,
            IsActive = true
        };
        await _userManager.CreateAsync(user, "OldPass123!");

        // 1st request
        var devLink1 = await _passwordResetService.RequestResetAsync("chef@qserve.local");
        var token1 = Uri.UnescapeDataString(devLink1!.Split("token=")[1]);

        // 2nd request (invalidates token1)
        var devLink2 = await _passwordResetService.RequestResetAsync("chef@qserve.local");
        var token2 = Uri.UnescapeDataString(devLink2!.Split("token=")[1]);

        // Attempting to reset with obsolete token1 must fail
        var result1 = await _passwordResetService.ResetPasswordAsync("chef@qserve.local", token1, "NewPassword123!");
        Assert.False(result1);

        // Attempting to reset with latest token2 must succeed
        var result2 = await _passwordResetService.ResetPasswordAsync("chef@qserve.local", token2, "NewPassword123!");
        Assert.True(result2);
    }

    [Fact]
    public async Task ResetPassword_WithTamperedToken_Fails()
    {
        var user = new User
        {
            FullName = "Chef Bob",
            Email = "chef@qserve.local",
            UserName = "chef@qserve.local",
            Role = UserRoles.Kitchen,
            IsActive = true
        };
        await _userManager.CreateAsync(user, "OldPass123!");

        var devLink = await _passwordResetService.RequestResetAsync("chef@qserve.local");
        var token = Uri.UnescapeDataString(devLink!.Split("token=")[1]);
        var tamperedToken = token + "_tampered_invalid";

        var result = await _passwordResetService.ResetPasswordAsync("chef@qserve.local", tamperedToken, "NewPassword123!");
        Assert.False(result);
    }

    [Fact]
    public async Task ResetPassword_AllowsLoginWithNewPassword_AndRejectsOldPassword()
    {
        var user = new User
        {
            FullName = "Chef Bob",
            Email = "chef@qserve.local",
            UserName = "chef@qserve.local",
            Role = UserRoles.Kitchen,
            IsActive = true
        };
        await _userManager.CreateAsync(user, "OldPass123!");

        var devLink = await _passwordResetService.RequestResetAsync("chef@qserve.local");
        var token = Uri.UnescapeDataString(devLink!.Split("token=")[1]);

        var resetSuccess = await _passwordResetService.ResetPasswordAsync("chef@qserve.local", token, "FreshSecurePass123!");
        Assert.True(resetSuccess);

        // Old password rejected
        var oldLoginResult = await _authService.ValidateLoginAsync("chef@qserve.local", "OldPass123!");
        Assert.Equal(LoginResult.InvalidCredentials, oldLoginResult.Result);

        // New password accepted
        var newLoginResult = await _authService.ValidateLoginAsync("chef@qserve.local", "FreshSecurePass123!");
        Assert.Equal(LoginResult.Success, newLoginResult.Result);
    }

    [Fact]
    public async Task ResetPassword_Controller_RejectsMismatchingPasswords_WithoutCallingService()
    {
        var controller = new AccountController(_authService, _passwordResetService, _signInManager, _userManager);
        var result = await controller.ResetPassword("test@qserve.local", "valid-token", "NewPassword123!", "MismatchPassword999!") as ViewResult;

        Assert.NotNull(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.Contains(controller.ModelState.Values.SelectMany(v => v.Errors), e => e.ErrorMessage == "Passwords do not match.");
    }

    [Fact]
    public async Task StaffManagement_CannotDeactivateOwnAccount()
    {
        var admin = new User
        {
            FullName = "Super Admin",
            Email = "admin@qserve.local",
            UserName = "admin@qserve.local",
            Role = UserRoles.Admin,
            IsActive = true
        };
        await _userManager.CreateAsync(admin, "StrongPass123!");

        var controller = new AdminStaffController(_db, _userManager);
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
            UserName = "admin1@qserve.local",
            Role = UserRoles.Admin,
            IsActive = true
        };
        var admin2 = new User
        {
            FullName = "Admin Two",
            Email = "admin2@qserve.local",
            UserName = "admin2@qserve.local",
            Role = UserRoles.Admin,
            IsActive = true
        };
        await _userManager.CreateAsync(admin1, "StrongPass123!");
        await _userManager.CreateAsync(admin2, "StrongPass123!");

        var controller = new AdminStaffController(_db, _userManager);
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

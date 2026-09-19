using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using QServe.Models;
using QServe.Services;

namespace QServe.Controllers;

public class AccountController : Controller
{
    private readonly IAuthService _authService;
    private readonly IPasswordResetService _passwordResetService;
    private readonly SignInManager<User> _signInManager;
    private readonly UserManager<User> _userManager;

    public AccountController(
        IAuthService authService,
        IPasswordResetService passwordResetService,
        SignInManager<User> signInManager,
        UserManager<User> userManager)
    {
        _authService = authService;
        _passwordResetService = passwordResetService;
        _signInManager = signInManager;
        _userManager = userManager;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToRoleHome(User.FindFirstValue(ClaimTypes.Role));

        return View();
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string email, string password)
    {
        var outcome = await _authService.ValidateLoginAsync(email, password);

        switch (outcome.Result)
        {
            case LoginResult.Success:
                var user = await _userManager.FindByIdAsync(outcome.UserId!.Value.ToString());
                if (user != null)
                {
                    await _signInManager.SignInAsync(user, isPersistent: false);
                }
                return RedirectToRoleHome(outcome.Role);

            case LoginResult.AccountLocked:
                ModelState.AddModelError(string.Empty, "This account is temporarily locked due to repeated failed attempts. Try again later.");
                return View();

            case LoginResult.AccountInactive:
                // Deliberately same generic message as invalid credentials — don't leak account state.
                ModelState.AddModelError(string.Empty, "Invalid email or password.");
                return View();

            default: // InvalidCredentials
                ModelState.AddModelError(string.Empty, "Invalid email or password.");
                return View();
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [Authorize]
    [HttpGet]
    public IActionResult AccessDenied() => View();

    // ---- Forgot / Reset Password (self-service) ----
    [HttpGet]
    [AllowAnonymous]
    public IActionResult ForgotPassword(string? email = null)
    {
        ViewBag.Email = email;
        return View();
    }

    [HttpPost]
    [ActionName("ForgotPassword")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPasswordConfirmed(string email)
    {
        var devLink = await _passwordResetService.RequestResetAsync(email);

        ViewBag.Submitted = true;
        ViewBag.DevResetLink = devLink;
        return View("ForgotPassword");
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult ResetPassword(string email, string token)
    {
        ViewBag.Email = email;
        ViewBag.Token = token;
        return View();
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(string email, string token, string newPassword, string confirmPassword)
    {
        // Validate that passwords match
        if (newPassword != confirmPassword)
        {
            ModelState.AddModelError(string.Empty, "Passwords do not match.");
            ViewBag.Email = email;
            ViewBag.Token = token;
            return View();
        }

        var success = await _passwordResetService.ResetPasswordAsync(email, token, newPassword);

        if (!success)
        {
            ModelState.AddModelError(string.Empty, "This reset link is invalid or has expired. Request a new one.");
            ViewBag.Email = email;
            ViewBag.Token = token;
            return View();
        }

        TempData["LoginMessage"] = "Your password has been reset. Log in with your new password.";
        return RedirectToAction(nameof(Login));
    }

    // AUTH-3: redirect by role — Admin/Manager to the dashboard, Kitchen to the KDS.
    private IActionResult RedirectToRoleHome(string? role) => role switch
    {
        UserRoles.Admin or UserRoles.Manager => RedirectToAction("Index", "Admin"),
        UserRoles.Kitchen => RedirectToAction("Index", "Kitchen"),
        _ => RedirectToAction(nameof(Login))
    };
}

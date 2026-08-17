using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QServe.Models;
using QServe.Services;

namespace QServe.Controllers;

public class AccountController : Controller
{
    private readonly IAuthService _authService;
    private readonly IPasswordResetService _passwordResetService;

    public AccountController(IAuthService authService, IPasswordResetService passwordResetService)
    {
        _authService = authService;
        _passwordResetService = passwordResetService;
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
                await SignInAsync(outcome.UserId!.Value, outcome.FullName!, outcome.Role!);
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
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [Authorize]
    [HttpGet]
    public IActionResult AccessDenied() => View();

    // ---- Forgot / Reset Password (self-service) ----
    // Admin-assisted reset for staff who can't self-serve still lives in
    // AdminStaffController.ResetPassword — this is the separate, unauthenticated path a staff
    // member uses on their own, from the login screen.

    [HttpGet]
    [AllowAnonymous]
    public IActionResult ForgotPassword() => View();

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(string email)
    {
        // The devLink is null in Production (and null in Development for a non-matching
        // email) — the view must not treat "no link shown" as proof the email didn't match.
        var devLink = await _passwordResetService.RequestResetAsync(email);

        ViewBag.Submitted = true;
        ViewBag.DevResetLink = devLink;
        return View();
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
    public async Task<IActionResult> ResetPassword(string email, string token, string newPassword)
    {
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

    private async Task SignInAsync(int userId, string fullName, string role)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, fullName),
            new(ClaimTypes.Role, role)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties
            {
                IsPersistent = false, // session cookie — closes when the browser closes
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            });
    }

    // AUTH-3: redirect by role — Admin/Manager to the dashboard, Kitchen to the KDS.
    private IActionResult RedirectToRoleHome(string? role) => role switch
    {
        UserRoles.Admin or UserRoles.Manager => RedirectToAction("Index", "Admin"),
        UserRoles.Kitchen => RedirectToAction("Index", "Kitchen"),
        _ => RedirectToAction(nameof(Login))
    };
}

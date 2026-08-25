using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QServe.Data;
using QServe.Models;

namespace QServe.Controllers;

/// <summary>
/// Module 8: staff account management (ADM-6). Admin ONLY — not Manager — per ADM-9. This is
/// a separate controller (rather than an action guarded with an inline role check inside
/// AdminController) specifically so the class-level [Authorize] is the whole story: nobody
/// has to trace through method bodies to know Manager can't reach any action here.
/// </summary>
[Authorize(Roles = UserRoles.Admin)]
public class AdminStaffController : Controller
{
    private readonly ApplicationDbContext _db;

    public AdminStaffController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var users = await _db.Users.OrderBy(u => u.FullName).ToListAsync();
        return View(users);
    }

    [HttpGet]
    public IActionResult Create() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string fullName, string email, string password, string role)
    {
        if (role is not (UserRoles.Admin or UserRoles.Kitchen or UserRoles.Manager))
        {
            ModelState.AddModelError(string.Empty, "Invalid role.");
            return View();
        }

        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
        {
            ModelState.AddModelError(string.Empty, "Password must be at least 8 characters.");
            return View();
        }

        if (await _db.Users.AnyAsync(u => u.Email == email))
        {
            // AUTH-2-style caution applies here too, though less critically since this is an
            // authenticated Admin-only action, not a public-facing one.
            ModelState.AddModelError(string.Empty, "A user with that email already exists.");
            return View();
        }

        var user = new User
        {
            FullName = fullName,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = role,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        await WriteAuditLogAsync("Users", user.UserID, "StaffAccountCreated", null,
            JsonSerializer.Serialize(new { user.FullName, user.Email, user.Role }));

        return RedirectToAction(nameof(Index));
    }

    // Gap fix: previously an admin could only change a name/email/role by deactivating and
    // recreating the account, which loses the UserID history behind existing AuditLogs and
    // Payments.VerifiedBy references. This edits the same row in place.
    [HttpGet]
    public async Task<IActionResult> Edit(int userId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null) return NotFound();
        return View(user);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int userId, string fullName, string email, string role)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null) return NotFound();

        if (role is not (UserRoles.Admin or UserRoles.Kitchen or UserRoles.Manager))
        {
            ModelState.AddModelError(string.Empty, "Invalid role.");
            return View(user);
        }

        if (await _db.Users.AnyAsync(u => u.Email == email && u.UserID != userId))
        {
            ModelState.AddModelError(string.Empty, "A different user already has that email.");
            return View(user);
        }

        var oldValue = JsonSerializer.Serialize(new { user.FullName, user.Email, user.Role });

        user.FullName = fullName;
        user.Email = email;
        user.Role = role;
        await _db.SaveChangesAsync();

        await WriteAuditLogAsync("Users", user.UserID, "StaffAccountEdited", oldValue,
            JsonSerializer.Serialize(new { user.FullName, user.Email, user.Role }));

        return RedirectToAction(nameof(Index));
    }

    // AUTH-7: deactivating here is what actually blocks login — AuthService already checks
    // IsActive on every attempt, so this action needs no extra enforcement of its own.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int userId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null) return NotFound();

        var currentAdminIdRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(currentAdminIdRaw, out var currentAdminId) && userId == currentAdminId && user.IsActive)
        {
            TempData["StaffMessage"] = "You cannot deactivate your own account while logged in.";
            return RedirectToAction(nameof(Index));
        }

        if (user.IsActive && user.Role == UserRoles.Admin)
        {
            var activeAdminCount = await _db.Users.CountAsync(u => u.IsActive && u.Role == UserRoles.Admin);
            if (activeAdminCount <= 1)
            {
                TempData["StaffMessage"] = "Cannot deactivate the last active Admin account.";
                return RedirectToAction(nameof(Index));
            }
        }

        var wasActive = user.IsActive;
        user.IsActive = !user.IsActive;
        await _db.SaveChangesAsync();

        await WriteAuditLogAsync("Users", user.UserID,
            wasActive ? "StaffAccountDeactivated" : "StaffAccountReactivated", null, null);

        return RedirectToAction(nameof(Index));
    }

    // Gap fix: previously a locked-out account (5 failed logins -> 15 min lockout, see
    // AuthService) had no recovery path except waiting. An Admin can now clear it immediately —
    // useful when the lockout is confirmed to be the legitimate user mistyping their password,
    // not an attack in progress.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearLockout(int userId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null) return NotFound();

        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
        await _db.SaveChangesAsync();

        await WriteAuditLogAsync("Users", user.UserID, "LockoutCleared", null, null);

        return RedirectToAction(nameof(Index));
    }

    // Gap fix: previously there was no password reset at all — a staff member who forgot their
    // password had no way back in. This is admin-assisted (the admin sets a temporary password
    // and communicates it out-of-band) rather than self-service email reset, which was
    // explicitly out of scope for v1 per the Module 2 PRD.
    [HttpGet]
    public async Task<IActionResult> ResetPassword(int userId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null) return NotFound();

        ViewBag.UserId = userId;
        ViewBag.FullName = user.FullName;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(int userId, string newPassword)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null) return NotFound();

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
        {
            ModelState.AddModelError(string.Empty, "Password must be at least 8 characters.");
            ViewBag.UserId = userId;
            ViewBag.FullName = user.FullName;
            return View();
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        // A fresh password shouldn't stay locked out behind old failed attempts.
        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
        await _db.SaveChangesAsync();

        await WriteAuditLogAsync("Users", user.UserID, "PasswordResetByAdmin", null, null);

        TempData["StaffMessage"] = $"Password reset for {user.FullName}. Share the new password with them directly — it isn't emailed.";
        return RedirectToAction(nameof(Index));
    }

    private async Task WriteAuditLogAsync(string entityType, int entityId, string action, string? oldValue, string? newValue)
    {
        var adminUserIdRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        int? performedBy = int.TryParse(adminUserIdRaw, out var id) ? id : null;

        _db.AuditLogs.Add(new AuditLog
        {
            EntityType = entityType,
            EntityID = entityId,
            Action = action,
            OldValue = oldValue,
            NewValue = newValue,
            PerformedBy = performedBy,
            Timestamp = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }
}

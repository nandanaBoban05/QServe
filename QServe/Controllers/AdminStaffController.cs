using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QServe.Data;
using QServe.Models;
using QServe.ViewModels;

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
    public async Task<IActionResult> Index([FromQuery] AdminStaffFilterViewModel filter)
    {
        var query = _db.Users.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            query = query.Where(u => u.FullName.Contains(search) || u.Email.Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(filter.Role) && filter.Role != "All")
        {
            query = query.Where(u => u.Role == filter.Role);
        }

        if (!string.IsNullOrWhiteSpace(filter.Status) && filter.Status != "All")
        {
            if (filter.Status == "Active") query = query.Where(u => u.IsActive);
            else if (filter.Status == "Deactivated") query = query.Where(u => !u.IsActive);
        }

        var totalCount = await query.CountAsync();
        var pageSize = filter.PageSize > 0 ? filter.PageSize : 20;
        var page = filter.Page > 0 ? filter.Page : 1;

        var users = await query
            .OrderBy(u => u.FullName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        filter.Users = new PagedResult<User>
        {
            Items = users,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };

        return View(filter);
    }

    [HttpGet]
    public IActionResult Create() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string fullName, string email, string password, string role)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            ModelState.AddModelError(string.Empty, "Full name is required.");
        }

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            ModelState.AddModelError(string.Empty, "A valid email address is required.");
        }

        if (role is not (UserRoles.Admin or UserRoles.Kitchen or UserRoles.Manager))
        {
            ModelState.AddModelError(string.Empty, "Invalid role.");
        }

        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
        {
            ModelState.AddModelError(string.Empty, "Password must be at least 8 characters.");
        }

        if (await _db.Users.AnyAsync(u => u.Email == email))
        {
            ModelState.AddModelError(string.Empty, "A user with that email already exists.");
        }

        if (!ModelState.IsValid)
        {
            return View();
        }

        var user = new User
        {
            FullName = fullName.Trim(),
            Email = email.Trim().ToLowerInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = role,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        await WriteAuditLogAsync("Users", user.UserID, "StaffAccountCreated", null,
            JsonSerializer.Serialize(new { user.FullName, user.Email, user.Role }));

        TempData["StaffMessage"] = $"Staff account created for {user.FullName} ({user.Role}).";
        return RedirectToAction(nameof(Index));
    }

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

        if (string.IsNullOrWhiteSpace(fullName))
        {
            ModelState.AddModelError(string.Empty, "Full name is required.");
        }

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            ModelState.AddModelError(string.Empty, "A valid email address is required.");
        }

        if (role is not (UserRoles.Admin or UserRoles.Kitchen or UserRoles.Manager))
        {
            ModelState.AddModelError(string.Empty, "Invalid role.");
        }

        if (await _db.Users.AnyAsync(u => u.Email == email && u.UserID != userId))
        {
            ModelState.AddModelError(string.Empty, "A different user already has that email.");
        }

        if (!ModelState.IsValid)
        {
            return View(user);
        }

        var oldValue = JsonSerializer.Serialize(new { user.FullName, user.Email, user.Role });

        user.FullName = fullName.Trim();
        user.Email = email.Trim().ToLowerInvariant();
        user.Role = role;
        await _db.SaveChangesAsync();

        await WriteAuditLogAsync("Users", user.UserID, "StaffAccountEdited", oldValue,
            JsonSerializer.Serialize(new { user.FullName, user.Email, user.Role }));

        TempData["StaffMessage"] = $"Staff account updated for {user.FullName}.";
        return RedirectToAction(nameof(Index));
    }

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

        TempData["StaffMessage"] = $"Account for {user.FullName} is now {(user.IsActive ? "Active" : "Deactivated")}.";
        return RedirectToAction(nameof(Index));
    }

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

        TempData["StaffMessage"] = $"Lockout cleared for {user.FullName}.";
        return RedirectToAction(nameof(Index));
    }

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
        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
        await _db.SaveChangesAsync();

        await WriteAuditLogAsync("Users", user.UserID, "PasswordResetByAdmin", null, null);

        TempData["StaffMessage"] = $"Password reset for {user.FullName}. Share the new password with them directly.";
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

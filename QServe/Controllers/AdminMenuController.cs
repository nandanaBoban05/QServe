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
/// Module 8: menu management (ADM-1/ADM-2). Split out from AdminController to keep each
/// controller focused — this one owns MenuCategories and MenuItems only.
/// Includes full audit logging for Menu creation, updates, deletion, and availability toggles.
/// </summary>
[Authorize(Roles = $"{UserRoles.Admin},{UserRoles.Manager}")]
public class AdminMenuController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;

    public AdminMenuController(ApplicationDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    private static readonly string[] AllowedImageExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
    private const long MaxImageBytes = 5 * 1024 * 1024; // 5 MB

    private async Task<(bool Success, string? RelativeUrl, string? Error)> SaveMenuItemImageAsync(IFormFile? imageFile)
    {
        if (imageFile is null || imageFile.Length == 0)
            return (true, null, null);

        var extension = Path.GetExtension(imageFile.FileName).ToLowerInvariant();
        if (!AllowedImageExtensions.Contains(extension))
            return (false, null, "Image must be a JPG, PNG, WEBP, or GIF file.");

        if (imageFile.Length > MaxImageBytes)
            return (false, null, "Image must be smaller than 5 MB.");

        var folder = Path.Combine(_env.WebRootPath, "images", "menu-items");
        Directory.CreateDirectory(folder);

        var fileName = $"{Guid.NewGuid():N}{extension}";
        var filePath = Path.Combine(folder, fileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await imageFile.CopyToAsync(stream);
        }

        return (true, $"/images/menu-items/{fileName}", null);
    }

    private void DeleteMenuItemImageFile(string? relativeUrl)
    {
        if (string.IsNullOrWhiteSpace(relativeUrl) || !relativeUrl.StartsWith("/images/menu-items/"))
            return;

        var fileName = Path.GetFileName(relativeUrl);
        var filePath = Path.Combine(_env.WebRootPath, "images", "menu-items", fileName);
        if (System.IO.File.Exists(filePath))
        {
            System.IO.File.Delete(filePath);
        }
    }

    // ---- Menu Items ----

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] AdminMenuFilterViewModel filter)
    {
        var query = _db.MenuItems.AsNoTracking()
            .Include(i => i.Category)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            query = query.Where(i => i.Name.Contains(search));
        }

        if (filter.CategoryId.HasValue && filter.CategoryId.Value > 0)
        {
            query = query.Where(i => i.CategoryID == filter.CategoryId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.ItemType) && filter.ItemType != "All")
        {
            query = query.Where(i => i.ItemType == filter.ItemType);
        }

        if (!string.IsNullOrWhiteSpace(filter.Availability) && filter.Availability != "All")
        {
            if (filter.Availability == "Available") query = query.Where(i => i.IsAvailable);
            else if (filter.Availability == "Unavailable") query = query.Where(i => !i.IsAvailable);
        }

        var totalCount = await query.CountAsync();
        var pageSize = filter.PageSize > 0 ? filter.PageSize : 10;
        var page = filter.Page > 0 ? filter.Page : 1;

        var items = await query
            .OrderBy(i => i.Category != null ? i.Category.DisplayOrder : 0)
            .ThenBy(i => i.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        filter.Items = new PagedResult<MenuItem>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };

        filter.Categories = await _db.MenuCategories.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.DisplayOrder)
            .ToListAsync();

        return View(filter);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleAvailability(int itemId)
    {
        var item = await _db.MenuItems.FindAsync(itemId);
        if (item is null) return NotFound();

        var wasAvailable = item.IsAvailable;
        item.IsAvailable = !item.IsAvailable;
        await _db.SaveChangesAsync();

        await WriteAuditLogAsync("MenuItems", item.ItemID, "MenuItemAvailabilityToggled",
            JsonSerializer.Serialize(new { IsAvailable = wasAvailable }),
            JsonSerializer.Serialize(new { IsAvailable = item.IsAvailable }));

        TempData["MenuSuccess"] = $"\"{item.Name}\" is now {(item.IsAvailable ? "Available" : "Unavailable")}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> CreateItem()
    {
        await PopulateCategoriesAsync();
        return View(new MenuItem());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateItem(MenuItem item, IFormFile? imageFile)
    {
        if (string.IsNullOrWhiteSpace(item.Name))
        {
            ModelState.AddModelError(nameof(item.Name), "Item name is required.");
        }

        if (item.Price <= 0)
        {
            ModelState.AddModelError(nameof(item.Price), "Price must be greater than zero.");
        }

        if (item.PrepTimeMinutes < 0)
        {
            ModelState.AddModelError(nameof(item.PrepTimeMinutes), "Prep time cannot be negative.");
        }

        if (!await _db.MenuCategories.AnyAsync(c => c.CategoryID == item.CategoryID))
        {
            ModelState.AddModelError(nameof(item.CategoryID), "Please select a valid category.");
        }

        if (!ModelState.IsValid)
        {
            await PopulateCategoriesAsync();
            return View(item);
        }

        var (imageSaved, imageUrl, imageError) = await SaveMenuItemImageAsync(imageFile);
        if (!imageSaved)
        {
            ModelState.AddModelError(nameof(item.ImageUrl), imageError!);
            await PopulateCategoriesAsync();
            return View(item);
        }

        item.Name = item.Name.Trim();
        item.CreatedAt = DateTime.UtcNow;
        item.TotalOrdered = 0;
        item.RecentOrdered = 0;
        item.ImageUrl = imageUrl;

        _db.MenuItems.Add(item);
        await _db.SaveChangesAsync();

        await WriteAuditLogAsync("MenuItems", item.ItemID, "MenuItemCreated", null,
            JsonSerializer.Serialize(new { item.Name, item.Price, item.CategoryID, item.ItemType, item.IsAvailable }));

        TempData["MenuSuccess"] = $"Menu item \"{item.Name}\" created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> EditItem(int itemId)
    {
        var item = await _db.MenuItems.FindAsync(itemId);
        if (item is null) return NotFound();

        await PopulateCategoriesAsync();
        return View(item);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditItem(int itemId, MenuItem updated, IFormFile? imageFile)
    {
        var item = await _db.MenuItems.FindAsync(itemId);
        if (item is null) return NotFound();

        if (string.IsNullOrWhiteSpace(updated.Name))
        {
            ModelState.AddModelError(nameof(updated.Name), "Item name is required.");
        }

        if (updated.Price <= 0)
        {
            ModelState.AddModelError(nameof(updated.Price), "Price must be greater than zero.");
        }

        if (updated.PrepTimeMinutes < 0)
        {
            ModelState.AddModelError(nameof(updated.PrepTimeMinutes), "Prep time cannot be negative.");
        }

        if (!await _db.MenuCategories.AnyAsync(c => c.CategoryID == updated.CategoryID))
        {
            ModelState.AddModelError(nameof(updated.CategoryID), "Please select a valid category.");
        }

        if (!ModelState.IsValid)
        {
            await PopulateCategoriesAsync();
            updated.ItemID = itemId;
            updated.ImageUrl = item.ImageUrl;
            return View(updated);
        }

        var oldValues = JsonSerializer.Serialize(new { item.Name, item.CategoryID, item.Price, item.PrepTimeMinutes, item.ItemType });

        var newImageUrl = item.ImageUrl;
        if (imageFile is not null && imageFile.Length > 0)
        {
            var (imageSaved, imageUrl, imageError) = await SaveMenuItemImageAsync(imageFile);
            if (!imageSaved)
            {
                ModelState.AddModelError(nameof(updated.ImageUrl), imageError!);
                await PopulateCategoriesAsync();
                updated.ItemID = itemId;
                updated.ImageUrl = item.ImageUrl;
                return View(updated);
            }

            DeleteMenuItemImageFile(item.ImageUrl);
            newImageUrl = imageUrl;
        }

        item.Name = updated.Name.Trim();
        item.CategoryID = updated.CategoryID;
        item.Price = updated.Price;
        item.PrepTimeMinutes = updated.PrepTimeMinutes;
        item.ItemType = updated.ItemType;
        item.ImageUrl = newImageUrl;

        await _db.SaveChangesAsync();

        await WriteAuditLogAsync("MenuItems", item.ItemID, "MenuItemUpdated", oldValues,
            JsonSerializer.Serialize(new { item.Name, item.CategoryID, item.Price, item.PrepTimeMinutes, item.ItemType }));

        TempData["MenuSuccess"] = $"Menu item \"{item.Name}\" updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteItem(int itemId)
    {
        var item = await _db.MenuItems.FindAsync(itemId);
        if (item is null) return NotFound();

        var orderedCount = await _db.OrderItems.CountAsync(oi => oi.ItemID == itemId);
        if (orderedCount > 0)
        {
            TempData["MenuError"] =
                $"Cannot delete \"{item.Name}\" — it appears in {orderedCount} past order line{(orderedCount == 1 ? "" : "s")}. " +
                "Mark it Unavailable instead to keep order history intact.";
            return RedirectToAction(nameof(Index));
        }

        DeleteMenuItemImageFile(item.ImageUrl);

        var itemName = item.Name;
        _db.MenuItems.Remove(item);
        await _db.SaveChangesAsync();

        await WriteAuditLogAsync("MenuItems", itemId, "MenuItemDeleted",
            JsonSerializer.Serialize(new { Name = itemName }), null);

        TempData["MenuSuccess"] = $"Menu item \"{itemName}\" deleted.";
        return RedirectToAction(nameof(Index));
    }

    // ---- Categories ----

    [HttpGet]
    public async Task<IActionResult> Categories([FromQuery] AdminCategoryFilterViewModel filter)
    {
        var query = _db.MenuCategories.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            query = query.Where(c => c.Name.Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(filter.Status) && filter.Status != "All")
        {
            if (filter.Status == "Active") query = query.Where(c => c.IsActive);
            else if (filter.Status == "Hidden") query = query.Where(c => !c.IsActive);
        }

        var totalCount = await query.CountAsync();
        var pageSize = filter.PageSize > 0 ? filter.PageSize : 10;
        var page = filter.Page > 0 ? filter.Page : 1;

        var categories = await query
            .Include(c => c.Items)
            .OrderBy(c => c.DisplayOrder)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        filter.Categories = new PagedResult<MenuCategory>
        {
            Items = categories,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };

        return View(filter);
    }

    [HttpGet]
    public IActionResult CreateCategory()
    {
        return View(new MenuCategory { DisplayOrder = 1, IsActive = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCategory(MenuCategory category)
    {
        if (string.IsNullOrWhiteSpace(category.Name))
        {
            ModelState.AddModelError(nameof(category.Name), "Category name is required.");
        }
        else
        {
            category.Name = category.Name.Trim();
            if (await _db.MenuCategories.AnyAsync(c => c.Name == category.Name))
            {
                ModelState.AddModelError(nameof(category.Name), $"Category \"{category.Name}\" already exists.");
            }
        }

        if (category.DisplayOrder < 1)
        {
            category.DisplayOrder = 1;
        }

        if (!ModelState.IsValid)
        {
            return View(category);
        }

        category.IsActive = true;

        _db.MenuCategories.Add(category);
        await _db.SaveChangesAsync();

        await WriteAuditLogAsync("MenuCategories", category.CategoryID, "MenuCategoryCreated", null,
            JsonSerializer.Serialize(new { category.Name, category.DisplayOrder, category.IsActive }));

        TempData["CategorySuccess"] = $"Category \"{category.Name}\" added.";
        return RedirectToAction(nameof(Categories));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditCategory(int categoryId, string name, int displayOrder)
    {
        var category = await _db.MenuCategories.FindAsync(categoryId);
        if (category is null) return NotFound();

        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["CategoryError"] = "Category name is required.";
            return RedirectToAction(nameof(Categories));
        }

        var trimmed = name.Trim();

        if (await _db.MenuCategories.AnyAsync(c => c.Name == trimmed && c.CategoryID != categoryId))
        {
            TempData["CategoryError"] = $"Category \"{trimmed}\" already exists.";
            return RedirectToAction(nameof(Categories));
        }

        var oldValues = JsonSerializer.Serialize(new { category.Name, category.DisplayOrder });

        category.Name = trimmed;
        category.DisplayOrder = displayOrder;
        await _db.SaveChangesAsync();

        await WriteAuditLogAsync("MenuCategories", category.CategoryID, "MenuCategoryUpdated", oldValues,
            JsonSerializer.Serialize(new { category.Name, category.DisplayOrder }));

        TempData["CategorySuccess"] = $"Category \"{trimmed}\" updated.";
        return RedirectToAction(nameof(Categories));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCategory(int categoryId)
    {
        var category = await _db.MenuCategories.FindAsync(categoryId);
        if (category is null) return NotFound();

        var itemCount = await _db.MenuItems.CountAsync(i => i.CategoryID == categoryId);
        if (itemCount > 0)
        {
            TempData["CategoryError"] =
                $"Cannot delete \"{category.Name}\" — it still has {itemCount} menu item{(itemCount == 1 ? "" : "s")}. " +
                "Move or delete those items first.";
            return RedirectToAction(nameof(Categories));
        }

        var categoryName = category.Name;
        _db.MenuCategories.Remove(category);
        await _db.SaveChangesAsync();

        await WriteAuditLogAsync("MenuCategories", categoryId, "MenuCategoryDeleted",
            JsonSerializer.Serialize(new { Name = categoryName }), null);

        TempData["CategorySuccess"] = $"Category \"{categoryName}\" deleted.";
        return RedirectToAction(nameof(Categories));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleCategoryActive(int categoryId)
    {
        var category = await _db.MenuCategories.FindAsync(categoryId);
        if (category is null) return NotFound();

        var wasActive = category.IsActive;
        category.IsActive = !category.IsActive;
        await _db.SaveChangesAsync();

        await WriteAuditLogAsync("MenuCategories", category.CategoryID, "MenuCategoryStatusToggled",
            JsonSerializer.Serialize(new { IsActive = wasActive }),
            JsonSerializer.Serialize(new { IsActive = category.IsActive }));

        TempData["CategorySuccess"] = $"Category \"{category.Name}\" is now {(category.IsActive ? "Active" : "Hidden")}.";
        return RedirectToAction(nameof(Categories));
    }

    private async Task PopulateCategoriesAsync()
    {
        ViewBag.Categories = await _db.MenuCategories.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.DisplayOrder)
            .ToListAsync();
    }

    private async Task WriteAuditLogAsync(string entityType, int entityId, string action, string? oldValue = null, string? newValue = null)
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
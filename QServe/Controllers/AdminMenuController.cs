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

    /// <summary>Saves an uploaded menu item image to wwwroot/images/menu-items and returns
    /// the relative URL to store in MenuItem.ImageUrl. Returns (true, null, null) if no
    /// file was provided — that's not an error, it just means "leave the image as-is".</summary>
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
        Directory.CreateDirectory(folder); // creates it on first run if it doesn't already exist

        var fileName = $"{Guid.NewGuid():N}{extension}";
        var filePath = Path.Combine(folder, fileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await imageFile.CopyToAsync(stream);
        }

        return (true, $"/images/menu-items/{fileName}", null);
    }

    /// <summary>Deletes a previously-uploaded menu item image file, if there is one. Only ever
    /// touches files under images/menu-items — an item whose ImageUrl is some other external
    /// URL (e.g. from before this change) is left alone.</summary>
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

    // ADM-2: real-time-ish availability toggle — reflected on the customer menu on its next
    // page load (Module 4 queries IsAvailable directly, no caching layer to invalidate).
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleAvailability(int itemId)
    {
        var item = await _db.MenuItems.FindAsync(itemId);
        if (item is null) return NotFound();

        item.IsAvailable = !item.IsAvailable;
        await _db.SaveChangesAsync();

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

        // Only attempt the upload once the rest of the form is valid, so we don't write an
        // orphaned file to disk for a submission that's about to be redisplayed anyway.
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
            updated.ImageUrl = item.ImageUrl; // keep showing the existing preview on validation failure
            return View(updated);
        }

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

            DeleteMenuItemImageFile(item.ImageUrl); // remove the old file now that the new one is saved
            newImageUrl = imageUrl;
        }

        item.Name = updated.Name.Trim();
        item.CategoryID = updated.CategoryID;
        item.Price = updated.Price;
        item.PrepTimeMinutes = updated.PrepTimeMinutes;
        item.ItemType = updated.ItemType;
        item.ImageUrl = newImageUrl;

        await _db.SaveChangesAsync();
        TempData["MenuSuccess"] = $"Menu item \"{item.Name}\" updated.";
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteItem(int itemId)
    {
        var item = await _db.MenuItems.FindAsync(itemId);
        if (item is null) return NotFound();

        // OrderItem.ItemID -> MenuItems is configured with DeleteBehavior.Restrict, so deleting
        // an item that has ever been ordered would fail at the database level. Block it
        // explicitly instead — past order/receipt history must stay intact.
        var orderedCount = await _db.OrderItems.CountAsync(oi => oi.ItemID == itemId);
        if (orderedCount > 0)
        {
            TempData["MenuError"] =
                $"Cannot delete \"{item.Name}\" — it appears in {orderedCount} past order line{(orderedCount == 1 ? "" : "s")}. " +
                "Mark it Unavailable instead to keep order history intact.";
            return RedirectToAction(nameof(Index));
        }

        DeleteMenuItemImageFile(item.ImageUrl);

        _db.MenuItems.Remove(item);
        await _db.SaveChangesAsync();

        TempData["MenuSuccess"] = $"Menu item \"{item.Name}\" deleted.";
        return RedirectToAction(nameof(Index));
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCategory(string name, int displayOrder)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["CategoryError"] = "Category name is required.";
            return RedirectToAction(nameof(Categories));
        }

        var trimmed = name.Trim();

        if (await _db.MenuCategories.AnyAsync(c => c.Name == trimmed))
        {
            TempData["CategoryError"] = $"Category \"{trimmed}\" already exists.";
            return RedirectToAction(nameof(Categories));
        }

        _db.MenuCategories.Add(new MenuCategory
        {
            Name = trimmed,
            DisplayOrder = displayOrder,
            IsActive = true
        });
        await _db.SaveChangesAsync();

        TempData["CategorySuccess"] = $"Category \"{trimmed}\" added.";
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

        // Exclude this category itself from the duplicate-name check so saving with an
        // unchanged name doesn't false-positive against its own current row.
        if (await _db.MenuCategories.AnyAsync(c => c.Name == trimmed && c.CategoryID != categoryId))
        {
            TempData["CategoryError"] = $"Category \"{trimmed}\" already exists.";
            return RedirectToAction(nameof(Categories));
        }

        category.Name = trimmed;
        category.DisplayOrder = displayOrder;
        await _db.SaveChangesAsync();

        TempData["CategorySuccess"] = $"Category \"{trimmed}\" updated.";
        return RedirectToAction(nameof(Categories));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCategory(int categoryId)
    {
        var category = await _db.MenuCategories.FindAsync(categoryId);
        if (category is null) return NotFound();

        // MenuItem.CategoryID is a required FK with no explicit delete behavior configured,
        // so EF Core's default is cascade delete — removing a category with items would
        // silently delete those items too. Block that explicitly instead.
        var itemCount = await _db.MenuItems.CountAsync(i => i.CategoryID == categoryId);
        if (itemCount > 0)
        {
            TempData["CategoryError"] =
                $"Cannot delete \"{category.Name}\" — it still has {itemCount} menu item{(itemCount == 1 ? "" : "s")}. " +
                "Move or delete those items first.";
            return RedirectToAction(nameof(Categories));
        }

        _db.MenuCategories.Remove(category);
        await _db.SaveChangesAsync();

        TempData["CategorySuccess"] = $"Category \"{category.Name}\" deleted.";
        return RedirectToAction(nameof(Categories));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleCategoryActive(int categoryId)
    {
        var category = await _db.MenuCategories.FindAsync(categoryId);
        if (category is null) return NotFound();

        category.IsActive = !category.IsActive;
        await _db.SaveChangesAsync();

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
}
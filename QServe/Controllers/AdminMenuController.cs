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

    public AdminMenuController(ApplicationDbContext db)
    {
        _db = db;
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
        var pageSize = filter.PageSize > 0 ? filter.PageSize : 20;
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
    public async Task<IActionResult> CreateItem(MenuItem item)
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

        item.Name = item.Name.Trim();
        item.CreatedAt = DateTime.UtcNow;
        item.TotalOrdered = 0;
        item.RecentOrdered = 0;

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
    public async Task<IActionResult> EditItem(int itemId, MenuItem updated)
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
            return View(updated);
        }

        item.Name = updated.Name.Trim();
        item.CategoryID = updated.CategoryID;
        item.Price = updated.Price;
        item.PrepTimeMinutes = updated.PrepTimeMinutes;
        item.ItemType = updated.ItemType;
        item.ImageUrl = updated.ImageUrl;

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

        filter.Categories = await query.OrderBy(c => c.DisplayOrder).ToListAsync();
        return View(filter);
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

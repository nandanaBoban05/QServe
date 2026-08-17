using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QServe.Data;
using QServe.Models;

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
    public async Task<IActionResult> Index()
    {
        var items = await _db.MenuItems
            .Include(i => i.Category)
            .OrderBy(i => i.Category!.DisplayOrder).ThenBy(i => i.Name)
            .ToListAsync();

        return View(items);
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
        // Deliberately not using full ModelState validation against the CartItem-style DTO
        // pattern here — MenuItem's DataAnnotations (from Module 1) already cover the basics
        // (required Name, MaxLength) and EF Core's CHECK constraints are the final backstop.
        if (item.Price <= 0)
        {
            ModelState.AddModelError(nameof(item.Price), "Price must be greater than zero.");
            await PopulateCategoriesAsync();
            return View(item);
        }

        item.CreatedAt = DateTime.UtcNow;
        item.TotalOrdered = 0;
        item.RecentOrdered = 0;

        _db.MenuItems.Add(item);
        await _db.SaveChangesAsync();

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

        if (updated.Price <= 0)
        {
            ModelState.AddModelError(nameof(updated.Price), "Price must be greater than zero.");
            await PopulateCategoriesAsync();
            updated.ItemID = itemId;
            return View(updated);
        }

        // Deliberately NOT touching TotalOrdered/RecentOrdered (Module 9 owns those) or
        // CreatedAt — this action only edits the fields an admin should actually change here.
        item.Name = updated.Name;
        item.CategoryID = updated.CategoryID;
        item.Price = updated.Price;
        item.PrepTimeMinutes = updated.PrepTimeMinutes;
        item.ItemType = updated.ItemType;
        item.ImageUrl = updated.ImageUrl;

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    // ---- Categories ----

    [HttpGet]
    public async Task<IActionResult> Categories()
    {
        var categories = await _db.MenuCategories.OrderBy(c => c.DisplayOrder).ToListAsync();
        return View(categories);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCategory(string name, int displayOrder)
    {
        if (string.IsNullOrWhiteSpace(name))
            return RedirectToAction(nameof(Categories));

        _db.MenuCategories.Add(new MenuCategory
        {
            Name = name.Trim(),
            DisplayOrder = displayOrder,
            IsActive = true
        });
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Categories));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleCategoryActive(int categoryId)
    {
        var category = await _db.MenuCategories.FindAsync(categoryId);
        if (category is null) return NotFound();

        // Module 4's menu query filters on Category.IsActive, so this hides the whole
        // category (and everything in it) from customers without deleting any data.
        category.IsActive = !category.IsActive;
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Categories));
    }

    private async Task PopulateCategoriesAsync()
    {
        ViewBag.Categories = await _db.MenuCategories.Where(c => c.IsActive).OrderBy(c => c.DisplayOrder).ToListAsync();
    }
}

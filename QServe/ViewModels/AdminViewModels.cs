using QServe.Models;

namespace QServe.ViewModels;

/// <summary>
/// Reusable generic container for paged data queries.
/// </summary>
public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalCount { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
    public bool HasPrevPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;
}

public class AdminOrderFilterViewModel
{
    public string? Search { get; set; }
    public int? TableId { get; set; }
    public string? OrderStatus { get; set; }
    public string? PaymentStatus { get; set; }
    public string? PaymentMode { get; set; }
    public string? OrderType { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    public PagedResult<Order> Orders { get; set; } = new();
    public List<RestaurantTable> Tables { get; set; } = new();
}

public class AdminOrderDetailsViewModel
{
    public Order Order { get; set; } = null!;
    public List<AuditLog> Timeline { get; set; } = new();
}

public class AdminPaymentFilterViewModel
{
    public string? Search { get; set; }
    public int? OrderId { get; set; }
    public string? PaymentStatus { get; set; }
    public string? PaymentMode { get; set; }
    public decimal? MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    public PagedResult<Payment> Payments { get; set; } = new();
}

public class AdminTableFilterViewModel
{
    public string? Search { get; set; }
    public string? Status { get; set; } // "All", "Active", "Inactive"
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    public PagedResult<RestaurantTable> Tables { get; set; } = new();
    public string BaseUrl { get; set; } = string.Empty;
    public Dictionary<int, string> TableQrUrls { get; set; } = new();
}

public class AdminMenuFilterViewModel
{
    public string? Search { get; set; }
    public int? CategoryId { get; set; }
    public string? ItemType { get; set; }
    public string? Availability { get; set; } // "All", "Available", "Unavailable"
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    public PagedResult<MenuItem> Items { get; set; } = new();
    public List<MenuCategory> Categories { get; set; } = new();
}

public class AdminCategoryFilterViewModel
{
    public string? Search { get; set; }
    public string? Status { get; set; } // "All", "Active", "Hidden"
    public List<MenuCategory> Categories { get; set; } = new();
}

public class AdminStaffFilterViewModel
{
    public string? Search { get; set; }
    public string? Role { get; set; }
    public string? Status { get; set; } // "All", "Active", "Deactivated"
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    public PagedResult<User> Users { get; set; } = new();
}

public class AdminAuditLogFilterViewModel
{
    public string? Search { get; set; }
    public string? EntityType { get; set; }
    public string? ActionName { get; set; }
    public int? UserId { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 30;

    public PagedResult<AuditLog> Logs { get; set; } = new();
    public List<string> EntityTypes { get; set; } = new();
}

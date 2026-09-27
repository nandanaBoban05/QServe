using System.ComponentModel.DataAnnotations;

namespace QServe.Models;

public class RestaurantTable
{
    [Key]
    public int TableID { get; set; }

    [Required, MaxLength(10)]
    public string TableNumber { get; set; } = string.Empty;

    [MaxLength(500)]
    public string QRCodeData { get; set; } = string.Empty;

    public int Capacity { get; set; } = 4;

    public bool IsActive { get; set; } = true;

    [MaxLength(100)]
    public string? QrTokenSalt { get; set; }

    // Navigation
    public ICollection<Order> Orders { get; set; } = new List<Order>();
}

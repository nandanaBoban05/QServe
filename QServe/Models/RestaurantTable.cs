using System.ComponentModel.DataAnnotations;

namespace QServe.Models;

public class RestaurantTable
{
    [Key]
    public int TableID { get; set; }

    [Required, MaxLength(10)]
    public string TableNumber { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string QRCodeData { get; set; } = string.Empty;

    public int Capacity { get; set; } = 4;

    public bool IsActive { get; set; } = true;

    // Navigation
    public ICollection<Order> Orders { get; set; } = new List<Order>();
}

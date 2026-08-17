using System.ComponentModel.DataAnnotations;

namespace QServe.Models;

public class MenuCategory
{
    [Key]
    public int CategoryID { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public int DisplayOrder { get; set; } = 0;

    public bool IsActive { get; set; } = true;

    // Navigation
    public ICollection<MenuItem> Items { get; set; } = new List<MenuItem>();
}

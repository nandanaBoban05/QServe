using Microsoft.EntityFrameworkCore;
using QServe.Models;

namespace QServe.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<RestaurantTable> RestaurantTables => Set<RestaurantTable>();
    public DbSet<MenuCategory> MenuCategories => Set<MenuCategory>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ---- Users ----
        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("CK_Users_Role",
                "\"Role\" IN ('Admin','Kitchen','Manager')"));
        });

        // ---- RestaurantTables ----
        modelBuilder.Entity<RestaurantTable>(e =>
        {
            e.HasIndex(t => t.TableNumber).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("CK_RestaurantTables_Capacity", "\"Capacity\" > 0"));
        });

        // ---- MenuCategories ----
        modelBuilder.Entity<MenuCategory>(e =>
        {
            e.HasIndex(c => c.Name).IsUnique();
        });

        // ---- MenuItems ----
        modelBuilder.Entity<MenuItem>(e =>
        {
            e.HasOne(m => m.Category)
             .WithMany(c => c.Items)
             .HasForeignKey(m => m.CategoryID)
             .OnDelete(DeleteBehavior.Restrict);

            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_MenuItems_Price", "\"Price\" > 0");
                t.HasCheckConstraint("CK_MenuItems_ItemType",
                    "\"ItemType\" IN ('Beverage','Cooked','Dessert','Quick')");
            });
        });

        // ---- Orders ----
        modelBuilder.Entity<Order>(e =>
        {
            e.HasOne(o => o.Table)
             .WithMany(t => t.Orders)
             .HasForeignKey(o => o.TableID)
             .OnDelete(DeleteBehavior.Restrict);

            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Orders_TotalAmount", "\"TotalAmount\" >= 0");
                t.HasCheckConstraint("CK_Orders_OrderStatus",
                    "\"OrderStatus\" IN ('PendingPayment','AwaitingVerification','Approved','Preparing','Ready','Served','Cancelled')");
                t.HasCheckConstraint("CK_Orders_OrderType", "\"OrderType\" IN ('Quick','Regular','Heavy')");
            });
        });

        // ---- OrderItems ----
        modelBuilder.Entity<OrderItem>(e =>
        {
            e.HasOne(oi => oi.Order)
             .WithMany(o => o.OrderItems)
             .HasForeignKey(oi => oi.OrderID)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(oi => oi.Item)
             .WithMany(m => m.OrderItems)
             .HasForeignKey(oi => oi.ItemID)
             .OnDelete(DeleteBehavior.Restrict);

            e.ToTable(t => t.HasCheckConstraint("CK_OrderItems_Quantity", "\"Quantity\" > 0"));
        });

        // ---- Payments ----
        modelBuilder.Entity<Payment>(e =>
        {
            e.HasIndex(p => p.OrderID).IsUnique(); // one payment per order

            e.HasOne(p => p.Order)
             .WithOne(o => o.Payment)
             .HasForeignKey<Payment>(p => p.OrderID)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(p => p.VerifiedByUser)
             .WithMany(u => u.VerifiedPayments)
             .HasForeignKey(p => p.VerifiedBy)
             .OnDelete(DeleteBehavior.Restrict);

            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Payments_PaymentMode", "\"PaymentMode\" IN ('Online','Cash','Card')");
                t.HasCheckConstraint("CK_Payments_PaymentStatus",
                    "\"PaymentStatus\" IN ('Pending','Processing','Received','Failed','Refunded')");
            });
        });

        // ---- AuditLogs ----
        modelBuilder.Entity<AuditLog>(e =>
        {
            e.HasOne(a => a.PerformedByUser)
             .WithMany(u => u.AuditLogs)
             .HasForeignKey(a => a.PerformedBy)
             .OnDelete(DeleteBehavior.SetNull);
        });

        SeedData(modelBuilder);
    }

    private static void SeedData(ModelBuilder modelBuilder)
    {
        // BCrypt hashes for default dev accounts (Admin123!, Kitchen123!, Manager123!).
        // Change these passwords before any shared or production deployment.
        modelBuilder.Entity<User>().HasData(new User
        {
            UserID = 1,
            FullName = "System Admin",
            Email = "admin@restaurant.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin123!"),
            Role = UserRoles.Admin,
            IsActive = true,
            CreatedAt = new DateTime(2026, 1, 1),
            AccessFailedCount = 0
        },
         new User
         {
             UserID = 2,
             FullName = "Kitchen Staff",
             Email = "kitchen@restaurant.local",
             PasswordHash = BCrypt.Net.BCrypt.HashPassword("Kitchen123!"),
             Role = UserRoles.Kitchen,
             IsActive = true,
             CreatedAt = new DateTime(2026, 1, 1),
             AccessFailedCount = 0
         },

        new User
        {
            UserID = 3,
            FullName = "Restaurant Manager",
            Email = "manager@restaurant.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Manager123!"),
            Role = UserRoles.Manager,
            IsActive = true,
            CreatedAt = new DateTime(2026, 1, 1),
            AccessFailedCount = 0
        });

        var tables = new[] { "T01", "T02", "T03", "T04", "T05" };
        for (int i = 0; i < tables.Length; i++)
        {
            modelBuilder.Entity<RestaurantTable>().HasData(new RestaurantTable
            {
                TableID = i + 1,
                TableNumber = tables[i],
                QRCodeData = $"https://localhost/order/table/{i + 1}", // regenerate via Module 3 once tokens exist
                Capacity = 4,
                IsActive = true
            });
        }

        modelBuilder.Entity<MenuCategory>().HasData(
            new MenuCategory { CategoryID = 1, Name = "Beverages", DisplayOrder = 1, IsActive = true },
            new MenuCategory { CategoryID = 2, Name = "Starters", DisplayOrder = 2, IsActive = true },
            new MenuCategory { CategoryID = 3, Name = "Main Course", DisplayOrder = 3, IsActive = true }
        );

        modelBuilder.Entity<MenuItem>().HasData(
            new MenuItem { ItemID = 1, CategoryID = 1, Name = "Masala Chai", Price = 40, ItemType = ItemTypes.Beverage, PrepTimeMinutes = 5, CreatedAt = new DateTime(2026, 1, 1) },
            new MenuItem { ItemID = 2, CategoryID = 1, Name = "Fresh Lime Soda", Price = 60, ItemType = ItemTypes.Beverage, PrepTimeMinutes = 5, CreatedAt = new DateTime(2026, 1, 1) },
            new MenuItem { ItemID = 3, CategoryID = 1, Name = "Cold Coffee", Price = 90, ItemType = ItemTypes.Beverage, PrepTimeMinutes = 5, CreatedAt = new DateTime(2026, 1, 1) },
            new MenuItem { ItemID = 4, CategoryID = 2, Name = "Veg Spring Rolls", Price = 150, ItemType = ItemTypes.Quick, PrepTimeMinutes = 10, CreatedAt = new DateTime(2026, 1, 1) },
            new MenuItem { ItemID = 5, CategoryID = 2, Name = "Chicken Satay", Price = 220, ItemType = ItemTypes.Cooked, PrepTimeMinutes = 15, CreatedAt = new DateTime(2026, 1, 1) },
            new MenuItem { ItemID = 6, CategoryID = 3, Name = "Paneer Butter Masala", Price = 260, ItemType = ItemTypes.Cooked, PrepTimeMinutes = 20, CreatedAt = new DateTime(2026, 1, 1) },
            new MenuItem { ItemID = 7, CategoryID = 3, Name = "Chicken Biryani", Price = 320, ItemType = ItemTypes.Cooked, PrepTimeMinutes = 25, CreatedAt = new DateTime(2026, 1, 1) },
            new MenuItem { ItemID = 8, CategoryID = 3, Name = "Grilled Fish", Price = 380, ItemType = ItemTypes.Cooked, PrepTimeMinutes = 22, CreatedAt = new DateTime(2026, 1, 1) },
            new MenuItem { ItemID = 9, CategoryID = 3, Name = "Gulab Jamun", Price = 90, ItemType = ItemTypes.Dessert, PrepTimeMinutes = 5, CreatedAt = new DateTime(2026, 1, 1) },
            new MenuItem { ItemID = 10, CategoryID = 3, Name = "Chocolate Brownie", Price = 140, ItemType = ItemTypes.Dessert, PrepTimeMinutes = 8, CreatedAt = new DateTime(2026, 1, 1) }
        );
    }
}

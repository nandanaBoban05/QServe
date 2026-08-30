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
            e.HasIndex(p => p.OrderID); // Non-unique index for fast order lookup & multiple attempts

            e.HasOne(p => p.Order)
             .WithMany(o => o.Payments)
             .HasForeignKey(p => p.OrderID)
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
                QRCodeData = string.Empty, // populated on first Admin -> Tables -> Generate/Regenerate QR     
                IsActive = true
            });
        }

        modelBuilder.Entity<MenuCategory>().HasData(
            new MenuCategory { CategoryID = 1, Name = "Beverages", DisplayOrder = 1, IsActive = true },
            new MenuCategory { CategoryID = 2, Name = "Starters", DisplayOrder = 2, IsActive = true },
            new MenuCategory { CategoryID = 3, Name = "Main Course", DisplayOrder = 3, IsActive = true }
        );

        modelBuilder.Entity<MenuItem>().HasData(
            new MenuItem { ItemID = 1, CategoryID = 1, Name = "Masala Chai", Price = 40, ItemType = ItemTypes.Beverage, PrepTimeMinutes = 5, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1576092768241-dec231879fc3?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 2, CategoryID = 1, Name = "Fresh Lime Soda", Price = 60, ItemType = ItemTypes.Beverage, PrepTimeMinutes = 5, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1513558161293-cdaf765ed2fd?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 3, CategoryID = 1, Name = "Cold Coffee", Price = 90, ItemType = ItemTypes.Beverage, PrepTimeMinutes = 5, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1517701604599-bb29b565090c?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 4, CategoryID = 2, Name = "Veg Spring Rolls", Price = 150, ItemType = ItemTypes.Quick, PrepTimeMinutes = 10, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1544025162-d76694265947?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 5, CategoryID = 2, Name = "Chicken Satay", Price = 220, ItemType = ItemTypes.Cooked, PrepTimeMinutes = 15, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1529193591184-b1d58069ecdd?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 6, CategoryID = 3, Name = "Paneer Butter Masala", Price = 260, ItemType = ItemTypes.Cooked, PrepTimeMinutes = 20, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1631452180519-c014fe946bc7?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 7, CategoryID = 3, Name = "Chicken Biryani", Price = 320, ItemType = ItemTypes.Cooked, PrepTimeMinutes = 25, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1563379091339-03b21ab4a4f8?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 8, CategoryID = 3, Name = "Grilled Fish", Price = 380, ItemType = ItemTypes.Cooked, PrepTimeMinutes = 22, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1519708227418-c8fd9a32b7a2?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 9, CategoryID = 3, Name = "Gulab Jamun", Price = 90, ItemType = ItemTypes.Dessert, PrepTimeMinutes = 5, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1527786356703-4b100091cd2c?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 10, CategoryID = 3, Name = "Chocolate Brownie", Price = 140, ItemType = ItemTypes.Dessert, PrepTimeMinutes = 8, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1606313564200-e75d5e30476c?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 11, CategoryID = 1, Name = "Mango Juice", Price = 80, ItemType = ItemTypes.Beverage, PrepTimeMinutes = 5, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1621506289937-a8e4df240d0b?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 12, CategoryID = 1, Name = "Watermelon Juice", Price = 70, ItemType = ItemTypes.Beverage, PrepTimeMinutes = 5, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1600271886742-f049cd451bba?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 13, CategoryID = 1, Name = "Iced Tea", Price = 75, ItemType = ItemTypes.Beverage, PrepTimeMinutes = 5, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1556679343-c7306c1976bc?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 14, CategoryID = 2, Name = "French Fries", Price = 120, ItemType = ItemTypes.Quick, PrepTimeMinutes = 10, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1573080496219-bb080dd4f877?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 15, CategoryID = 2, Name = "Veg Burger", Price = 160, ItemType = ItemTypes.Quick, PrepTimeMinutes = 12, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1568901346375-23c9450c58cd?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 16, CategoryID = 2, Name = "Chicken Burger", Price = 190, ItemType = ItemTypes.Quick, PrepTimeMinutes = 15, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1565299507177-b0ac66763828?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 17, CategoryID = 2, Name = "Chicken Nuggets", Price = 180, ItemType = ItemTypes.Quick, PrepTimeMinutes = 12, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1562967914-608f82629710?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 18, CategoryID = 3, Name = "Veg Fried Rice", Price = 180, ItemType = ItemTypes.Cooked, PrepTimeMinutes = 18, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1512058564366-18510be2db19?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 19, CategoryID = 3, Name = "Chicken Fried Rice", Price = 240, ItemType = ItemTypes.Cooked, PrepTimeMinutes = 20, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1603133872878-684f208fb84b?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 20, CategoryID = 3, Name = "Veg Noodles", Price = 170, ItemType = ItemTypes.Cooked, PrepTimeMinutes = 15, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1552611052-33e04de081de?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 22, CategoryID = 3, Name = "Butter Chicken", Price = 340, ItemType = ItemTypes.Cooked, PrepTimeMinutes = 25, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1603894584373-5ac82b2ae398?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 23, CategoryID = 3, Name = "Chocolate Ice Cream", Price = 110, ItemType = ItemTypes.Dessert, PrepTimeMinutes = 3, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1570197788417-0e82375c9371?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 24, CategoryID = 3, Name = "Vanilla Ice Cream", Price = 100, ItemType = ItemTypes.Dessert, PrepTimeMinutes = 3, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1563805042-7684c019e1cb?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 25, CategoryID = 3, Name = "Cheesecake", Price = 180, ItemType = ItemTypes.Dessert, PrepTimeMinutes = 8, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1565958011703-44f9829ba187?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 26, CategoryID = 1, Name = "Masala Buttermilk", Price = 50, ItemType = ItemTypes.Beverage, PrepTimeMinutes = 3, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1523677011781-c91d1bbe2f9e?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 27, CategoryID = 1, Name = "Sweet Lassi", Price = 80, ItemType = ItemTypes.Beverage, PrepTimeMinutes = 5, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1626201850125-18d2d9a17c5b?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 28, CategoryID = 1, Name = "Mango Lassi", Price = 100, ItemType = ItemTypes.Beverage, PrepTimeMinutes = 5, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1621506289937-a8e4df240d0b?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 29, CategoryID = 2, Name = "Samosa", Price = 60, ItemType = ItemTypes.Quick, PrepTimeMinutes = 8, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1601050690597-df0568f70950?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 30, CategoryID = 2, Name = "Paneer Tikka", Price = 220, ItemType = ItemTypes.Quick, PrepTimeMinutes = 15, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1599487488170-d11ec9c172f0?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 31, CategoryID = 2, Name = "Chicken 65", Price = 240, ItemType = ItemTypes.Quick, PrepTimeMinutes = 15, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1585937421612-70a008356fbe?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 32, CategoryID = 2, Name = "Vegetable Pakora", Price = 100, ItemType = ItemTypes.Quick, PrepTimeMinutes = 10, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1625944525533-473f1a3d54e7?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 33, CategoryID = 3, Name = "Kerala Parotta", Price = 20, ItemType = ItemTypes.Quick, PrepTimeMinutes = 5, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1601050690117-94f5f6fa8bd5?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 34, CategoryID = 3, Name = "Kadai Paneer", Price = 280, ItemType = ItemTypes.Cooked, PrepTimeMinutes = 20, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1567188040759-fb8a883dc6d8?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 35, CategoryID = 3, Name = "Palak Paneer", Price = 260, ItemType = ItemTypes.Cooked, PrepTimeMinutes = 20, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1596797038530-2c107229654b?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 36, CategoryID = 3, Name = "Chicken Curry", Price = 280, ItemType = ItemTypes.Cooked, PrepTimeMinutes = 25, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1603894584373-5ac82b2ae398?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 37, CategoryID = 3, Name = "Kerala Fish Curry", Price = 300, ItemType = ItemTypes.Cooked, PrepTimeMinutes = 25, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1625944525945-7d8b40f95f69?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 38, CategoryID = 3, Name = "Mutton Rogan Josh", Price = 420, ItemType = ItemTypes.Cooked, PrepTimeMinutes = 30, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1545247181-516773cae754?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 39, CategoryID = 3, Name = "Appam with Chicken Stew", Price = 250, ItemType = ItemTypes.Cooked, PrepTimeMinutes = 20, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1603894584373-5ac82b2ae398?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 40, CategoryID = 3, Name = "Payasam", Price = 100, ItemType = ItemTypes.Dessert, PrepTimeMinutes = 5, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1601050690597-df0568f70950?w=500&auto=format&fit=crop&q=60" },
            new MenuItem { ItemID = 41, CategoryID = 3, Name = "Rasgulla", Price = 90, ItemType = ItemTypes.Dessert, PrepTimeMinutes = 5, CreatedAt = new DateTime(2026, 1, 1), ImageUrl = "https://images.unsplash.com/photo-1571115764595-644a1f56a55c?w=500&auto=format&fit=crop&q=60" }
                    );

    }
}

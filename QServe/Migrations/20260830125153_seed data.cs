using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace QServe.Migrations
{
    /// <inheritdoc />
    public partial class seeddata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MenuCategories",
                columns: table => new
                {
                    CategoryID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuCategories", x => x.CategoryID);
                });

            migrationBuilder.CreateTable(
                name: "RestaurantTables",
                columns: table => new
                {
                    TableID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TableNumber = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    QRCodeData = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Capacity = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestaurantTables", x => x.TableID);
                    table.CheckConstraint("CK_RestaurantTables_Capacity", "\"Capacity\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    UserID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FullName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false),
                    LockoutEnd = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PasswordResetTokenHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PasswordResetTokenExpiry = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.UserID);
                    table.CheckConstraint("CK_Users_Role", "\"Role\" IN ('Admin','Kitchen','Manager')");
                });

            migrationBuilder.CreateTable(
                name: "MenuItems",
                columns: table => new
                {
                    ItemID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CategoryID = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    PrepTimeMinutes = table.Column<int>(type: "int", nullable: false),
                    ItemType = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    IsAvailable = table.Column<bool>(type: "bit", nullable: false),
                    TotalOrdered = table.Column<int>(type: "int", nullable: false),
                    RecentOrdered = table.Column<int>(type: "int", nullable: false),
                    ImageUrl = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuItems", x => x.ItemID);
                    table.CheckConstraint("CK_MenuItems_ItemType", "\"ItemType\" IN ('Beverage','Cooked','Dessert','Quick')");
                    table.CheckConstraint("CK_MenuItems_Price", "\"Price\" > 0");
                    table.ForeignKey(
                        name: "FK_MenuItems_MenuCategories_CategoryID",
                        column: x => x.CategoryID,
                        principalTable: "MenuCategories",
                        principalColumn: "CategoryID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    OrderID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TableID = table.Column<int>(type: "int", nullable: false),
                    OrderStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OrderType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ServedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.OrderID);
                    table.CheckConstraint("CK_Orders_OrderStatus", "\"OrderStatus\" IN ('PendingPayment','AwaitingVerification','Approved','Preparing','Ready','Served','Cancelled')");
                    table.CheckConstraint("CK_Orders_OrderType", "\"OrderType\" IN ('Quick','Regular','Heavy')");
                    table.CheckConstraint("CK_Orders_TotalAmount", "\"TotalAmount\" >= 0");
                    table.ForeignKey(
                        name: "FK_Orders_RestaurantTables_TableID",
                        column: x => x.TableID,
                        principalTable: "RestaurantTables",
                        principalColumn: "TableID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    LogID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntityType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EntityID = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OldValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PerformedBy = table.Column<int>(type: "int", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.LogID);
                    table.ForeignKey(
                        name: "FK_AuditLogs_Users_PerformedBy",
                        column: x => x.PerformedBy,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "OrderItems",
                columns: table => new
                {
                    OrderItemID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderID = table.Column<int>(type: "int", nullable: false),
                    ItemID = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    Customization = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderItems", x => x.OrderItemID);
                    table.CheckConstraint("CK_OrderItems_Quantity", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_OrderItems_MenuItems_ItemID",
                        column: x => x.ItemID,
                        principalTable: "MenuItems",
                        principalColumn: "ItemID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderItems_Orders_OrderID",
                        column: x => x.OrderID,
                        principalTable: "Orders",
                        principalColumn: "OrderID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    PaymentID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderID = table.Column<int>(type: "int", nullable: false),
                    PaymentMode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    PaymentStatus = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    RazorpayOrderID = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RazorpayPaymentID = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    VerifiedBy = table.Column<int>(type: "int", nullable: true),
                    VerificationTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.PaymentID);
                    table.CheckConstraint("CK_Payments_PaymentMode", "\"PaymentMode\" IN ('Online','Cash','Card')");
                    table.CheckConstraint("CK_Payments_PaymentStatus", "\"PaymentStatus\" IN ('Pending','Processing','Received','Failed','Refunded')");
                    table.ForeignKey(
                        name: "FK_Payments_Orders_OrderID",
                        column: x => x.OrderID,
                        principalTable: "Orders",
                        principalColumn: "OrderID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Payments_Users_VerifiedBy",
                        column: x => x.VerifiedBy,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "MenuCategories",
                columns: new[] { "CategoryID", "DisplayOrder", "IsActive", "Name" },
                values: new object[,]
                {
                    { 1, 1, true, "Beverages" },
                    { 2, 2, true, "Starters" },
                    { 3, 3, true, "Main Course" }
                });

            migrationBuilder.InsertData(
                table: "RestaurantTables",
                columns: new[] { "TableID", "Capacity", "IsActive", "QRCodeData", "TableNumber" },
                values: new object[,]
                {
                    { 1, 4, true, "", "T01" },
                    { 2, 4, true, "", "T02" },
                    { 3, 4, true, "", "T03" },
                    { 4, 4, true, "", "T04" },
                    { 5, 4, true, "", "T05" }
                });

            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "UserID", "AccessFailedCount", "CreatedAt", "Email", "FullName", "IsActive", "LockoutEnd", "PasswordHash", "PasswordResetTokenExpiry", "PasswordResetTokenHash", "Role" },
                values: new object[,]
                {
                    { 1, 0, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "admin@restaurant.local", "System Admin", true, null, "$2a$11$QMyrjjsBLb7obUvnOtfbiu.H5O0IrMWVqsAOCG0OS4xuUkUsj/ct2", null, null, "Admin" },
                    { 2, 0, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "kitchen@restaurant.local", "Kitchen Staff", true, null, "$2a$11$iXo607uYZzXx9Lves8pkEeihb0WrXiAYfO0ka./e1.hKTKt3T554W", null, null, "Kitchen" },
                    { 3, 0, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "manager@restaurant.local", "Restaurant Manager", true, null, "$2a$11$GQqWprbnqdgn/88W318q1.CIMOKhOgJYQo9F.wij6ERJMS4H5hgve", null, null, "Manager" }
                });

            migrationBuilder.InsertData(
                table: "MenuItems",
                columns: new[] { "ItemID", "CategoryID", "CreatedAt", "ImageUrl", "IsAvailable", "ItemType", "Name", "PrepTimeMinutes", "Price", "RecentOrdered", "TotalOrdered" },
                values: new object[,]
                {
                    { 1, 1, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1576092768241-dec231879fc3?w=500&auto=format&fit=crop&q=60", true, "Beverage", "Masala Chai", 5, 40m, 0, 0 },
                    { 2, 1, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1513558161293-cdaf765ed2fd?w=500&auto=format&fit=crop&q=60", true, "Beverage", "Fresh Lime Soda", 5, 60m, 0, 0 },
                    { 3, 1, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1517701604599-bb29b565090c?w=500&auto=format&fit=crop&q=60", true, "Beverage", "Cold Coffee", 5, 90m, 0, 0 },
                    { 4, 2, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1544025162-d76694265947?w=500&auto=format&fit=crop&q=60", true, "Quick", "Veg Spring Rolls", 10, 150m, 0, 0 },
                    { 5, 2, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1529193591184-b1d58069ecdd?w=500&auto=format&fit=crop&q=60", true, "Cooked", "Chicken Satay", 15, 220m, 0, 0 },
                    { 6, 3, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1631452180519-c014fe946bc7?w=500&auto=format&fit=crop&q=60", true, "Cooked", "Paneer Butter Masala", 20, 260m, 0, 0 },
                    { 7, 3, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1563379091339-03b21ab4a4f8?w=500&auto=format&fit=crop&q=60", true, "Cooked", "Chicken Biryani", 25, 320m, 0, 0 },
                    { 8, 3, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1519708227418-c8fd9a32b7a2?w=500&auto=format&fit=crop&q=60", true, "Cooked", "Grilled Fish", 22, 380m, 0, 0 },
                    { 9, 3, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1527786356703-4b100091cd2c?w=500&auto=format&fit=crop&q=60", true, "Dessert", "Gulab Jamun", 5, 90m, 0, 0 },
                    { 10, 3, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1606313564200-e75d5e30476c?w=500&auto=format&fit=crop&q=60", true, "Dessert", "Chocolate Brownie", 8, 140m, 0, 0 },
                    { 11, 1, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1621506289937-a8e4df240d0b?w=500&auto=format&fit=crop&q=60", true, "Beverage", "Mango Juice", 5, 80m, 0, 0 },
                    { 12, 1, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1600271886742-f049cd451bba?w=500&auto=format&fit=crop&q=60", true, "Beverage", "Watermelon Juice", 5, 70m, 0, 0 },
                    { 13, 1, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1556679343-c7306c1976bc?w=500&auto=format&fit=crop&q=60", true, "Beverage", "Iced Tea", 5, 75m, 0, 0 },
                    { 14, 2, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1573080496219-bb080dd4f877?w=500&auto=format&fit=crop&q=60", true, "Quick", "French Fries", 10, 120m, 0, 0 },
                    { 15, 2, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1568901346375-23c9450c58cd?w=500&auto=format&fit=crop&q=60", true, "Quick", "Veg Burger", 12, 160m, 0, 0 },
                    { 16, 2, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1565299507177-b0ac66763828?w=500&auto=format&fit=crop&q=60", true, "Quick", "Chicken Burger", 15, 190m, 0, 0 },
                    { 17, 2, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1562967914-608f82629710?w=500&auto=format&fit=crop&q=60", true, "Quick", "Chicken Nuggets", 12, 180m, 0, 0 },
                    { 18, 3, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1512058564366-18510be2db19?w=500&auto=format&fit=crop&q=60", true, "Cooked", "Veg Fried Rice", 18, 180m, 0, 0 },
                    { 19, 3, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1603133872878-684f208fb84b?w=500&auto=format&fit=crop&q=60", true, "Cooked", "Chicken Fried Rice", 20, 240m, 0, 0 },
                    { 20, 3, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1552611052-33e04de081de?w=500&auto=format&fit=crop&q=60", true, "Cooked", "Veg Noodles", 15, 170m, 0, 0 },
                    { 22, 3, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1603894584373-5ac82b2ae398?w=500&auto=format&fit=crop&q=60", true, "Cooked", "Butter Chicken", 25, 340m, 0, 0 },
                    { 23, 3, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1570197788417-0e82375c9371?w=500&auto=format&fit=crop&q=60", true, "Dessert", "Chocolate Ice Cream", 3, 110m, 0, 0 },
                    { 24, 3, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1563805042-7684c019e1cb?w=500&auto=format&fit=crop&q=60", true, "Dessert", "Vanilla Ice Cream", 3, 100m, 0, 0 },
                    { 25, 3, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1565958011703-44f9829ba187?w=500&auto=format&fit=crop&q=60", true, "Dessert", "Cheesecake", 8, 180m, 0, 0 },
                    { 26, 1, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1523677011781-c91d1bbe2f9e?w=500&auto=format&fit=crop&q=60", true, "Beverage", "Masala Buttermilk", 3, 50m, 0, 0 },
                    { 27, 1, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1626201850125-18d2d9a17c5b?w=500&auto=format&fit=crop&q=60", true, "Beverage", "Sweet Lassi", 5, 80m, 0, 0 },
                    { 28, 1, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1621506289937-a8e4df240d0b?w=500&auto=format&fit=crop&q=60", true, "Beverage", "Mango Lassi", 5, 100m, 0, 0 },
                    { 29, 2, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1601050690597-df0568f70950?w=500&auto=format&fit=crop&q=60", true, "Quick", "Samosa", 8, 60m, 0, 0 },
                    { 30, 2, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1599487488170-d11ec9c172f0?w=500&auto=format&fit=crop&q=60", true, "Quick", "Paneer Tikka", 15, 220m, 0, 0 },
                    { 31, 2, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1585937421612-70a008356fbe?w=500&auto=format&fit=crop&q=60", true, "Quick", "Chicken 65", 15, 240m, 0, 0 },
                    { 32, 2, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1625944525533-473f1a3d54e7?w=500&auto=format&fit=crop&q=60", true, "Quick", "Vegetable Pakora", 10, 100m, 0, 0 },
                    { 33, 3, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1601050690117-94f5f6fa8bd5?w=500&auto=format&fit=crop&q=60", true, "Quick", "Kerala Parotta", 5, 20m, 0, 0 },
                    { 34, 3, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1567188040759-fb8a883dc6d8?w=500&auto=format&fit=crop&q=60", true, "Cooked", "Kadai Paneer", 20, 280m, 0, 0 },
                    { 35, 3, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1596797038530-2c107229654b?w=500&auto=format&fit=crop&q=60", true, "Cooked", "Palak Paneer", 20, 260m, 0, 0 },
                    { 36, 3, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1603894584373-5ac82b2ae398?w=500&auto=format&fit=crop&q=60", true, "Cooked", "Chicken Curry", 25, 280m, 0, 0 },
                    { 37, 3, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1625944525945-7d8b40f95f69?w=500&auto=format&fit=crop&q=60", true, "Cooked", "Kerala Fish Curry", 25, 300m, 0, 0 },
                    { 38, 3, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1545247181-516773cae754?w=500&auto=format&fit=crop&q=60", true, "Cooked", "Mutton Rogan Josh", 30, 420m, 0, 0 },
                    { 39, 3, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1603894584373-5ac82b2ae398?w=500&auto=format&fit=crop&q=60", true, "Cooked", "Appam with Chicken Stew", 20, 250m, 0, 0 },
                    { 40, 3, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1601050690597-df0568f70950?w=500&auto=format&fit=crop&q=60", true, "Dessert", "Payasam", 5, 100m, 0, 0 },
                    { 41, 3, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1571115764595-644a1f56a55c?w=500&auto=format&fit=crop&q=60", true, "Dessert", "Rasgulla", 5, 90m, 0, 0 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_PerformedBy",
                table: "AuditLogs",
                column: "PerformedBy");

            migrationBuilder.CreateIndex(
                name: "IX_MenuCategories_Name",
                table: "MenuCategories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MenuItems_CategoryID",
                table: "MenuItems",
                column: "CategoryID");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_ItemID",
                table: "OrderItems",
                column: "ItemID");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_OrderID",
                table: "OrderItems",
                column: "OrderID");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TableID",
                table: "Orders",
                column: "TableID");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_OrderID",
                table: "Payments",
                column: "OrderID");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_VerifiedBy",
                table: "Payments",
                column: "VerifiedBy");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantTables_TableNumber",
                table: "RestaurantTables",
                column: "TableNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "OrderItems");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "MenuItems");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "MenuCategories");

            migrationBuilder.DropTable(
                name: "RestaurantTables");
        }
    }
}

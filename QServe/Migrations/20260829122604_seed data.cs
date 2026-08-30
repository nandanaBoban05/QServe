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
            migrationBuilder.DropIndex(
                name: "IX_Payments_OrderID",
                table: "Payments");

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "Payments",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 1,
                column: "ImageUrl",
                value: "https://images.unsplash.com/photo-1576092768241-dec231879fc3?w=500&auto=format&fit=crop&q=60");

            migrationBuilder.UpdateData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 2,
                column: "ImageUrl",
                value: "https://images.unsplash.com/photo-1513558161293-cdaf765ed2fd?w=500&auto=format&fit=crop&q=60");

            migrationBuilder.UpdateData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 3,
                column: "ImageUrl",
                value: "https://images.unsplash.com/photo-1517701604599-bb29b565090c?w=500&auto=format&fit=crop&q=60");

            migrationBuilder.UpdateData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 4,
                column: "ImageUrl",
                value: "https://images.unsplash.com/photo-1544025162-d76694265947?w=500&auto=format&fit=crop&q=60");

            migrationBuilder.UpdateData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 5,
                column: "ImageUrl",
                value: "https://images.unsplash.com/photo-1529193591184-b1d58069ecdd?w=500&auto=format&fit=crop&q=60");

            migrationBuilder.UpdateData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 6,
                column: "ImageUrl",
                value: "https://images.unsplash.com/photo-1631452180519-c014fe946bc7?w=500&auto=format&fit=crop&q=60");

            migrationBuilder.UpdateData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 7,
                column: "ImageUrl",
                value: "https://images.unsplash.com/photo-1563379091339-03b21ab4a4f8?w=500&auto=format&fit=crop&q=60");

            migrationBuilder.UpdateData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 8,
                column: "ImageUrl",
                value: "https://images.unsplash.com/photo-1519708227418-c8fd9a32b7a2?w=500&auto=format&fit=crop&q=60");

            migrationBuilder.UpdateData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 9,
                column: "ImageUrl",
                value: "https://images.unsplash.com/photo-1527786356703-4b100091cd2c?w=500&auto=format&fit=crop&q=60");

            migrationBuilder.UpdateData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 10,
                column: "ImageUrl",
                value: "https://images.unsplash.com/photo-1606313564200-e75d5e30476c?w=500&auto=format&fit=crop&q=60");

            migrationBuilder.InsertData(
                table: "MenuItems",
                columns: new[] { "ItemID", "CategoryID", "CreatedAt", "ImageUrl", "IsAvailable", "ItemType", "Name", "PrepTimeMinutes", "Price", "RecentOrdered", "TotalOrdered" },
                values: new object[,]
                {
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
                    { 21, 3, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "https://images.unsplash.com/photo-1585032226651-759b368d7246?w=500&auto=format&fit=crop&q=60", true, "Cooked", "Chicken Noodles", 20, 230m, 0, 0 },
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

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "UserID",
                keyValue: 1,
                column: "PasswordHash",
                value: "$2a$11$nPKn7yUv1JMWALFKgggF/.VM9XOhmvFGAMN./hSXZ.8zeOKyvRmJG");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "UserID",
                keyValue: 2,
                column: "PasswordHash",
                value: "$2a$11$vSH1GMys6Zlhe25fKCb8oeai5j/pWb1eh96bJFlG4Cth5HY9Ncb/e");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "UserID",
                keyValue: 3,
                column: "PasswordHash",
                value: "$2a$11$HdNbWIG2BSgTdcHQ7eQDxujg.zy6/16LgPC3AbBriZXZPojU368xm");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_OrderID",
                table: "Payments",
                column: "OrderID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payments_OrderID",
                table: "Payments");

            migrationBuilder.DeleteData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 11);

            migrationBuilder.DeleteData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 12);

            migrationBuilder.DeleteData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 13);

            migrationBuilder.DeleteData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 14);

            migrationBuilder.DeleteData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 15);

            migrationBuilder.DeleteData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 16);

            migrationBuilder.DeleteData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 17);

            migrationBuilder.DeleteData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 18);

            migrationBuilder.DeleteData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 19);

            migrationBuilder.DeleteData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 20);

            migrationBuilder.DeleteData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 21);

            migrationBuilder.DeleteData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 22);

            migrationBuilder.DeleteData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 23);

            migrationBuilder.DeleteData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 24);

            migrationBuilder.DeleteData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 25);

            migrationBuilder.DeleteData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 26);

            migrationBuilder.DeleteData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 27);

            migrationBuilder.DeleteData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 28);

            migrationBuilder.DeleteData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 29);

            migrationBuilder.DeleteData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 30);

            migrationBuilder.DeleteData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 31);

            migrationBuilder.DeleteData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 32);

            migrationBuilder.DeleteData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 33);

            migrationBuilder.DeleteData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 34);

            migrationBuilder.DeleteData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 35);

            migrationBuilder.DeleteData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 36);

            migrationBuilder.DeleteData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 37);

            migrationBuilder.DeleteData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 38);

            migrationBuilder.DeleteData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 39);

            migrationBuilder.DeleteData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 40);

            migrationBuilder.DeleteData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 41);

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "Payments");

            migrationBuilder.UpdateData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 1,
                column: "ImageUrl",
                value: null);

            migrationBuilder.UpdateData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 2,
                column: "ImageUrl",
                value: null);

            migrationBuilder.UpdateData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 3,
                column: "ImageUrl",
                value: null);

            migrationBuilder.UpdateData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 4,
                column: "ImageUrl",
                value: null);

            migrationBuilder.UpdateData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 5,
                column: "ImageUrl",
                value: null);

            migrationBuilder.UpdateData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 6,
                column: "ImageUrl",
                value: null);

            migrationBuilder.UpdateData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 7,
                column: "ImageUrl",
                value: null);

            migrationBuilder.UpdateData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 8,
                column: "ImageUrl",
                value: null);

            migrationBuilder.UpdateData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 9,
                column: "ImageUrl",
                value: null);

            migrationBuilder.UpdateData(
                table: "MenuItems",
                keyColumn: "ItemID",
                keyValue: 10,
                column: "ImageUrl",
                value: null);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "UserID",
                keyValue: 1,
                column: "PasswordHash",
                value: "$2a$11$kXuZdaLFL/N7Ql7si/bEB.YqvX4BZRHgTZ3eqJM1PQxd8.I/rcBQm");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "UserID",
                keyValue: 2,
                column: "PasswordHash",
                value: "$2a$11$3LZFAGx2iQQjujrZ5a/aGO/PJ/YcwWvkT5B5kDtSqAHc.uEZcANnm");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "UserID",
                keyValue: 3,
                column: "PasswordHash",
                value: "$2a$11$W33uoBcNZfQbHJam8NPprOyr55RzKbQ31iBcMJcya5g3OThckg28.");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_OrderID",
                table: "Payments",
                column: "OrderID",
                unique: true);
        }
    }
}

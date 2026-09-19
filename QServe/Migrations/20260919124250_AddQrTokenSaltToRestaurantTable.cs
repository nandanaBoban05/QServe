using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QServe.Migrations
{
    /// <inheritdoc />
    public partial class AddQrTokenSaltToRestaurantTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "QrTokenSalt",
                table: "RestaurantTables",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "RestaurantTables",
                keyColumn: "TableID",
                keyValue: 1,
                column: "QrTokenSalt",
                value: null);

            migrationBuilder.UpdateData(
                table: "RestaurantTables",
                keyColumn: "TableID",
                keyValue: 2,
                column: "QrTokenSalt",
                value: null);

            migrationBuilder.UpdateData(
                table: "RestaurantTables",
                keyColumn: "TableID",
                keyValue: 3,
                column: "QrTokenSalt",
                value: null);

            migrationBuilder.UpdateData(
                table: "RestaurantTables",
                keyColumn: "TableID",
                keyValue: 4,
                column: "QrTokenSalt",
                value: null);

            migrationBuilder.UpdateData(
                table: "RestaurantTables",
                keyColumn: "TableID",
                keyValue: 5,
                column: "QrTokenSalt",
                value: null);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEJoyIDmPexCGsaYWWSMI+ziUYXM7Z8JX/s3xAteNzcf/oICIDFOzD6xfkaxc9cIWYw==");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 2,
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEKARyV97cSRfLNZSuIH9C/c/vvwdOEuc7EU84LbHXHRqZgTN5Y2+BOqla2kaKGSveg==");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 3,
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEMikNOfy6DZpIb3ehMRffG+ZhAADJ6NAfCheEC+3baHKfz9ggEFGb8BiPUUeuOvv6Q==");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "QrTokenSalt",
                table: "RestaurantTables");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEK6uDIJG076xXsQWr/EBvpAVpkif9vdNUMbYDbEWqXLeZBSffqySVTzEep+a2zCkgQ==");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 2,
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEF8sfRoTc55iqwy7BVGC+J5VzyMeDHQCLPCH9GjE2ZEy7WFkGVSSL94iEIp+o9/KAg==");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 3,
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEM9rwe3x5+Z3nrOXsnliWWasSaY67sQqAj9ppIQpBC9snQ3yu9sI/gQ8hmsMsmfAfg==");
        }
    }
}

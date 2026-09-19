using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QServe.Migrations
{
    /// <inheritdoc />
    public partial class qrissue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEI7deRVroGLHy6YQT4vo7Fq46Jw3nN9Io2hRZwkfeeyeYaoCd7AtK+yUvlmeP7GeCw==");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 2,
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEGTBhh8DZonzMpfqI+ncGtB/nvv78R4+VwoHT11l64g1AWeG4dncRMVfseIl8DaJTA==");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 3,
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEA5AG/idzRdJkML0p4Owk9HFDQLE3SXF1vO1DvtfTHWd14X5B0/1/M0y0mc9B5WKkw==");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QServe.Migrations
{
    /// <inheritdoc />
    public partial class initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEPF08DvKk5R/zEarz/lCDubkpP26XU3IkY3zje8lS792IukUEi6VMqY7FQZi8KvPpw==");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 2,
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEK3yOJaKzL80fyuTrMsUgi67FDMMK2y7Xy9G03zYCu4U1yuiME80t0ECjy38fSqKxQ==");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 3,
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEOJhklCtRkjdOuPJzSpY7T88VTNVLrwvBDP9XKDCVbJa4eUNv/d1055mTQwN1XZzhA==");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEAS+V6am5Lwo5aSKAcK/2y1+sQyaqVharM+zj+trMoEaKS8QwXVFIQaIkdfrRmdRJQ==");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 2,
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEF18y93Nqj4h6Do8reKHO344lViz/rIo4f4/o5Q8d/s6zSpXvJD1I9RfFtjVPruzRg==");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 3,
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEA8oTHtVwUZk9Z44KLrudqHHNca84qNMbnxMCMwW/eeGzME7uQFhzNU6Ag4U+r5Iyw==");
        }
    }
}

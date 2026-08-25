using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QServe.Migrations
{
    /// <inheritdoc />
    public partial class txt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "UserID",
                keyValue: 1,
                column: "PasswordHash",
                value: "$2a$11$qeuWfT3Trc9jwgc/c1wiuu976WqXO.Ky7vkyDPXbC8yp49IQW5svi");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "UserID",
                keyValue: 2,
                column: "PasswordHash",
                value: "$2a$11$r/EF2qcOpYImHhRJ/gPanO4wjQQLubCc98GFRmkTl5pCFpZs3GAGG");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "UserID",
                keyValue: 3,
                column: "PasswordHash",
                value: "$2a$11$rwv6qCoAlVJavgawV22e7.HXpQ0dM4sPFpyF7squOu1YKFXOs6JZO");
        }
    }
}

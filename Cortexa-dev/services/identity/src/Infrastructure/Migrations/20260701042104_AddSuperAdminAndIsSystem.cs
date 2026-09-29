using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cortexa.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSuperAdminAndIsSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_users_role",
                table: "users");

            migrationBuilder.AddColumn<bool>(
                name: "is_system",
                table: "users",
                type: "BOOL",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_role",
                table: "users",
                sql: "role IN ('Researcher','Reviewer','Admin','SuperAdmin')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_users_role",
                table: "users");

            migrationBuilder.DropColumn(
                name: "is_system",
                table: "users");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_role",
                table: "users",
                sql: "role IN ('Researcher','Reviewer','Admin')");
        }
    }
}

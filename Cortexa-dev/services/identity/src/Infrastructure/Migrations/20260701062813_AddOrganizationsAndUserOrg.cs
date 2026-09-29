using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cortexa.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationsAndUserOrg : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "org_id",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "organizations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "VARCHAR(200)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "TIMESTAMPTZ", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "TIMESTAMPTZ", nullable: false, defaultValueSql: "now()"),
                    deleted_at = table.Column<DateTimeOffset>(type: "TIMESTAMPTZ", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organizations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_users_org_id",
                table: "users",
                column: "org_id");

            migrationBuilder.CreateIndex(
                name: "IX_organizations_name",
                table: "organizations",
                column: "name",
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_users_organizations_org_id",
                table: "users",
                column: "org_id",
                principalTable: "organizations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_users_organizations_org_id",
                table: "users");

            migrationBuilder.DropTable(
                name: "organizations");

            migrationBuilder.DropIndex(
                name: "IX_users_org_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "org_id",
                table: "users");
        }
    }
}

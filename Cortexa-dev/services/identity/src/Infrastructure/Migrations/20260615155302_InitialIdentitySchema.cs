using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cortexa.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialIdentitySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "VARCHAR(320)", nullable: false),
                    username = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    password_hash = table.Column<string>(type: "TEXT", nullable: true),
                    role = table.Column<string>(type: "VARCHAR(20)", nullable: false, defaultValue: "Researcher"),
                    entra_object_id = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "TIMESTAMPTZ", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                    table.CheckConstraint("ck_users_role", "role IN ('Researcher','Reviewer','Admin')");
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "TEXT", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "TIMESTAMPTZ", nullable: false),
                    revoked = table.Column<bool>(type: "BOOL", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "TIMESTAMPTZ", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "FK_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_expiry",
                table: "refresh_tokens",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user",
                table: "refresh_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_entra_object_id",
                table: "users",
                column: "entra_object_id",
                unique: true,
                filter: "entra_object_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_users_username",
                table: "users",
                column: "username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cortexa.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountProtections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "security_stamp",
                table: "users",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.Sql(
                """
                UPDATE users SET security_stamp = gen_random_uuid();
                """);

            migrationBuilder.CreateTable(
                name: "failed_login_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    lockout_count = table.Column<int>(type: "integer", nullable: false),
                    window_started_at = table.Column<DateTimeOffset>(type: "TIMESTAMPTZ", nullable: false),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "TIMESTAMPTZ", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "TIMESTAMPTZ", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_failed_login_attempts", x => x.id);
                    table.ForeignKey(
                        name: "FK_failed_login_attempts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_failed_login_attempts_user_id",
                table: "failed_login_attempts",
                column: "user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "failed_login_attempts");

            migrationBuilder.DropColumn(
                name: "security_stamp",
                table: "users");
        }
    }
}

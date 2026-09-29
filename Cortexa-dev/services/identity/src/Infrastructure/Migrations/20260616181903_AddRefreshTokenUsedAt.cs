using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cortexa.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRefreshTokenUsedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "used_at",
                table: "refresh_tokens",
                type: "TIMESTAMPTZ",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "used_at",
                table: "refresh_tokens");
        }
    }
}

using Cortexa.Identity.Domain;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cortexa.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BackfillDefaultOrganization : Migration
    {
        private static readonly string DefaultOrganizationId = OrganizationConstants.DefaultOrganizationId.ToString();
        private const string DefaultOrganizationName = OrganizationConstants.DefaultOrganizationName;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                INSERT INTO organizations (id, name, created_at, updated_at, deleted_at)
                VALUES ('{DefaultOrganizationId}', '{DefaultOrganizationName}', now(), now(), NULL)
                ON CONFLICT (id) DO NOTHING;
                """);

            migrationBuilder.Sql($"""
                UPDATE users
                SET org_id = '{DefaultOrganizationId}'
                WHERE role <> 'SuperAdmin' AND org_id IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                UPDATE users
                SET org_id = NULL
                WHERE org_id = '{DefaultOrganizationId}';
                """);

            migrationBuilder.Sql($"""
                DELETE FROM organizations
                WHERE id = '{DefaultOrganizationId}';
                """);
        }
    }
}

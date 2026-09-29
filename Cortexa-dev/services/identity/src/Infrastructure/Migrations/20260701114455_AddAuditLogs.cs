using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cortexa.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    event_type = table.Column<string>(type: "TEXT", nullable: false),
                    resource_type = table.Column<string>(type: "TEXT", nullable: false),
                    resource_id = table.Column<string>(type: "TEXT", nullable: true),
                    action = table.Column<string>(type: "TEXT", nullable: false),
                    details = table.Column<string>(type: "TEXT", nullable: true),
                    created_date = table.Column<DateTimeOffset>(type: "TIMESTAMPTZ", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_logs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_created_date",
                table: "audit_logs",
                column: "created_date");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_event_type",
                table: "audit_logs",
                column: "event_type");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_user",
                table: "audit_logs",
                column: "user_id");

            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION audit_logs_block_mutation()
                RETURNS TRIGGER AS $$
                BEGIN
                    RAISE EXCEPTION 'audit_logs is append-only: % is not permitted', TG_OP;
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER audit_logs_block_update
                    BEFORE UPDATE ON audit_logs
                    FOR EACH ROW
                    EXECUTE FUNCTION audit_logs_block_mutation();
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER audit_logs_block_delete
                    BEFORE DELETE ON audit_logs
                    FOR EACH ROW
                    EXECUTE FUNCTION audit_logs_block_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS audit_logs_block_update ON audit_logs;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS audit_logs_block_delete ON audit_logs;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS audit_logs_block_mutation();");

            migrationBuilder.DropTable(
                name: "audit_logs");
        }
    }
}

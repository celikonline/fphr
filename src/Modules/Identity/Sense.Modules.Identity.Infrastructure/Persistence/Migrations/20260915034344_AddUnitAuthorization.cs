using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUnitAuthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "unit_authorizations",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    can_view_salary_and_payments = table.Column<bool>(type: "boolean", nullable: false),
                    can_view_unit_reports = table.Column<bool>(type: "boolean", nullable: false),
                    granted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    granted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unit_authorizations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_unit_authorizations_org_unit_id",
                schema: "identity",
                table: "unit_authorizations",
                column: "org_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_unit_authorizations_tenant_id",
                schema: "identity",
                table: "unit_authorizations",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_unit_authorizations_tenant_id_org_unit_id_user_id",
                schema: "identity",
                table: "unit_authorizations",
                columns: new[] { "tenant_id", "org_unit_id", "user_id" },
                unique: true,
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_unit_authorizations_user_id",
                schema: "identity",
                table: "unit_authorizations",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "unit_authorizations",
                schema: "identity");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Organization.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrgUnitTypeSortOrderNonUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_org_unit_types_tenant_id_sort_order",
                schema: "org",
                table: "org_unit_types");

            migrationBuilder.CreateIndex(
                name: "ix_org_unit_types_tenant_id_sort_order",
                schema: "org",
                table: "org_unit_types",
                columns: new[] { "tenant_id", "sort_order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_org_unit_types_tenant_id_sort_order",
                schema: "org",
                table: "org_unit_types");

            migrationBuilder.CreateIndex(
                name: "ix_org_unit_types_tenant_id_sort_order",
                schema: "org",
                table: "org_unit_types",
                columns: new[] { "tenant_id", "sort_order" },
                unique: true);
        }
    }
}

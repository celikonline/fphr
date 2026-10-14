using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Organization.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrgUnitCostCenterAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "cost_center_id",
                schema: "org",
                table: "org_units",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "employee_cost_center_overrides",
                schema: "org",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cost_center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_cost_center_overrides", x => x.id);
                    table.ForeignKey(
                        name: "fk_employee_cost_center_overrides_cost_centers_cost_center_id",
                        column: x => x.cost_center_id,
                        principalSchema: "org",
                        principalTable: "cost_centers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_org_units_cost_center_id",
                schema: "org",
                table: "org_units",
                column: "cost_center_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_cost_center_overrides_cost_center_id",
                schema: "org",
                table: "employee_cost_center_overrides",
                column: "cost_center_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_cost_center_overrides_tenant_id",
                schema: "org",
                table: "employee_cost_center_overrides",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_cost_center_overrides_tenant_id_employee_id",
                schema: "org",
                table: "employee_cost_center_overrides",
                columns: new[] { "tenant_id", "employee_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_org_units_cost_centers_cost_center_id",
                schema: "org",
                table: "org_units",
                column: "cost_center_id",
                principalSchema: "org",
                principalTable: "cost_centers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_org_units_cost_centers_cost_center_id",
                schema: "org",
                table: "org_units");

            migrationBuilder.DropTable(
                name: "employee_cost_center_overrides",
                schema: "org");

            migrationBuilder.DropIndex(
                name: "ix_org_units_cost_center_id",
                schema: "org",
                table: "org_units");

            migrationBuilder.DropColumn(
                name: "cost_center_id",
                schema: "org",
                table: "org_units");
        }
    }
}

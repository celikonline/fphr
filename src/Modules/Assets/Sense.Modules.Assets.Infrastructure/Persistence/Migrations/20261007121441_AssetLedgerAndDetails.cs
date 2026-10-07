using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Assets.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssetLedgerAndDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "asset_tag",
                schema: "assets",
                table: "assets",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "brand",
                schema: "assets",
                table: "assets",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "condition",
                schema: "assets",
                table: "assets",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Good");

            migrationBuilder.AddColumn<string>(
                name: "model",
                schema: "assets",
                table: "assets",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "purchase_cost",
                schema: "assets",
                table: "assets",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "purchase_date",
                schema: "assets",
                table: "assets",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "warranty_ends_on",
                schema: "assets",
                table: "assets",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "asset_assignments",
                schema: "assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    issued_at = table.Column<DateOnly>(type: "date", nullable: false),
                    returned_at = table.Column<DateOnly>(type: "date", nullable: true),
                    end_reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    return_condition = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    acknowledged_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asset_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_asset_assignments_assets_asset_id",
                        column: x => x.asset_id,
                        principalSchema: "assets",
                        principalTable: "assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assets_tenant_id_asset_tag",
                schema: "assets",
                table: "assets",
                columns: new[] { "tenant_id", "asset_tag" },
                unique: true,
                filter: "asset_tag IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_asset_assignments_asset_id",
                schema: "assets",
                table: "asset_assignments",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_asset_assignments_tenant_id",
                schema: "assets",
                table: "asset_assignments",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_asset_assignments_tenant_id_asset_id",
                schema: "assets",
                table: "asset_assignments",
                columns: new[] { "tenant_id", "asset_id" });

            migrationBuilder.CreateIndex(
                name: "ix_asset_assignments_tenant_id_asset_id1",
                schema: "assets",
                table: "asset_assignments",
                columns: new[] { "tenant_id", "asset_id" },
                unique: true,
                filter: "returned_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_asset_assignments_tenant_id_employee_id",
                schema: "assets",
                table: "asset_assignments",
                columns: new[] { "tenant_id", "employee_id" });

            // Hand-added backfill: every asset that is Assigned today gets exactly one open ledger row carrying the
            // asset's own tenant_id (the new table starts empty, so without this the history would lose current holders).
            migrationBuilder.Sql(
                """
                INSERT INTO assets.asset_assignments (id, tenant_id, asset_id, employee_id, issued_at, created_at)
                SELECT gen_random_uuid(), a.tenant_id, a.id, a.assigned_employee_id, COALESCE(a.issued_at, a.created_at::date), now()
                FROM assets.assets a
                WHERE a.status = 'Assigned' AND a.assigned_employee_id IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "asset_assignments",
                schema: "assets");

            migrationBuilder.DropIndex(
                name: "ix_assets_tenant_id_asset_tag",
                schema: "assets",
                table: "assets");

            migrationBuilder.DropColumn(
                name: "asset_tag",
                schema: "assets",
                table: "assets");

            migrationBuilder.DropColumn(
                name: "brand",
                schema: "assets",
                table: "assets");

            migrationBuilder.DropColumn(
                name: "condition",
                schema: "assets",
                table: "assets");

            migrationBuilder.DropColumn(
                name: "model",
                schema: "assets",
                table: "assets");

            migrationBuilder.DropColumn(
                name: "purchase_cost",
                schema: "assets",
                table: "assets");

            migrationBuilder.DropColumn(
                name: "purchase_date",
                schema: "assets",
                table: "assets");

            migrationBuilder.DropColumn(
                name: "warranty_ends_on",
                schema: "assets",
                table: "assets");
        }
    }
}

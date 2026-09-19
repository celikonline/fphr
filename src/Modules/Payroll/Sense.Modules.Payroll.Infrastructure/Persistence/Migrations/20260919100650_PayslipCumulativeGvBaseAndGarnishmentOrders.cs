using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Payroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PayslipCumulativeGvBaseAndGarnishmentOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "cumulative_gv_base",
                schema: "payroll",
                table: "payslips",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "garnishment_orders",
                schema: "payroll",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    case_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    monthly_installment_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    remaining_balance = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    closed_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_garnishment_orders", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_garnishment_orders_tenant_id",
                schema: "payroll",
                table: "garnishment_orders",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_garnishment_orders_tenant_id_employee_id_closed_date",
                schema: "payroll",
                table: "garnishment_orders",
                columns: new[] { "tenant_id", "employee_id", "closed_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "garnishment_orders",
                schema: "payroll");

            migrationBuilder.DropColumn(
                name: "cumulative_gv_base",
                schema: "payroll",
                table: "payslips");
        }
    }
}

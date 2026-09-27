using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Payroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayslipDeliveries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payslip_deliveries",
                schema: "payroll",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payslip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    payslip_version = table.Column<int>(type: "integer", nullable: false),
                    viewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    evidence_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payslip_deliveries", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payslip_deliveries_payslip_id",
                schema: "payroll",
                table: "payslip_deliveries",
                column: "payslip_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payslip_deliveries_tenant_id",
                schema: "payroll",
                table: "payslip_deliveries",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_payslip_deliveries_tenant_id_employee_id",
                schema: "payroll",
                table: "payslip_deliveries",
                columns: new[] { "tenant_id", "employee_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payslip_deliveries",
                schema: "payroll");
        }
    }
}

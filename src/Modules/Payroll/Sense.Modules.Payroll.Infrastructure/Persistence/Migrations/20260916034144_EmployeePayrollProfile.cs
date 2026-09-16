using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Payroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EmployeePayrollProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "employee_payroll_profiles",
                schema: "payroll",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sgk_workplace_company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sgk_workplace_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    wage_basis = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    sgk_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    incentive_law_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    meal_benefit_variant = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    meal_daily_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    transport_variant = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    payment_method = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sick_days_reduce_pay = table.Column<bool>(type: "boolean", nullable: false),
                    external_payroll_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_payroll_profiles", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_employee_payroll_profiles_tenant_id",
                schema: "payroll",
                table: "employee_payroll_profiles",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_payroll_profiles_tenant_id_employee_id_valid_from",
                schema: "payroll",
                table: "employee_payroll_profiles",
                columns: new[] { "tenant_id", "employee_id", "valid_from" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_payroll_profiles",
                schema: "payroll");
        }
    }
}

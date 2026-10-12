using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Payroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payroll_pay_item_definitions",
                schema: "payroll",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    direction = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    amount_basis = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    is_sgk_subject = table.Column<bool>(type: "boolean", nullable: false),
                    is_income_tax_subject = table.Column<bool>(type: "boolean", nullable: false),
                    is_stamp_tax_subject = table.Column<bool>(type: "boolean", nullable: false),
                    formula = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    default_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_pay_item_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payroll_pay_item_assignments",
                schema: "payroll",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pay_item_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    valid_from_month = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to_month = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_pay_item_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_payroll_pay_item_assignments_pay_item_definitions_pay_item_",
                        column: x => x.pay_item_definition_id,
                        principalSchema: "payroll",
                        principalTable: "payroll_pay_item_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_pay_item_assignments_pay_item_definition_id",
                schema: "payroll",
                table: "payroll_pay_item_assignments",
                column: "pay_item_definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_pay_item_assignments_tenant_id",
                schema: "payroll",
                table: "payroll_pay_item_assignments",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_pay_item_assignments_tenant_id_employee_id_valid_fr",
                schema: "payroll",
                table: "payroll_pay_item_assignments",
                columns: new[] { "tenant_id", "employee_id", "valid_from_month" });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_pay_item_definitions_tenant_id",
                schema: "payroll",
                table: "payroll_pay_item_definitions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_pay_item_definitions_tenant_id_code",
                schema: "payroll",
                table: "payroll_pay_item_definitions",
                columns: new[] { "tenant_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payroll_pay_item_assignments",
                schema: "payroll");

            migrationBuilder.DropTable(
                name: "payroll_pay_item_definitions",
                schema: "payroll");
        }
    }
}

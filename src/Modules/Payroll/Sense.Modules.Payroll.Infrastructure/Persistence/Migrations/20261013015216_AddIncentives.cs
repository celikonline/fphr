using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Payroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIncentives : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payroll_incentive_definitions",
                schema: "payroll",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    law_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    declaration_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    max_months = table.Column<int>(type: "integer", nullable: true),
                    is_smmm_approved = table.Column<bool>(type: "boolean", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approval_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    template_key = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parameters = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_incentive_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payroll_incentive_assignments",
                schema: "payroll",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incentive_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valid_from_month = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to_month = table.Column<DateOnly>(type: "date", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_incentive_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_payroll_incentive_assignments_incentive_definitions_incenti",
                        column: x => x.incentive_definition_id,
                        principalSchema: "payroll",
                        principalTable: "payroll_incentive_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_incentive_assignments_incentive_definition_id",
                schema: "payroll",
                table: "payroll_incentive_assignments",
                column: "incentive_definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_incentive_assignments_tenant_id",
                schema: "payroll",
                table: "payroll_incentive_assignments",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_incentive_assignments_tenant_id_employee_id_valid_f",
                schema: "payroll",
                table: "payroll_incentive_assignments",
                columns: new[] { "tenant_id", "employee_id", "valid_from_month" });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_incentive_definitions_tenant_id",
                schema: "payroll",
                table: "payroll_incentive_definitions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_incentive_definitions_tenant_id_code",
                schema: "payroll",
                table: "payroll_incentive_definitions",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payroll_incentive_definitions_tenant_id_template_key",
                schema: "payroll",
                table: "payroll_incentive_definitions",
                columns: new[] { "tenant_id", "template_key" },
                unique: true,
                filter: "template_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payroll_incentive_assignments",
                schema: "payroll");

            migrationBuilder.DropTable(
                name: "payroll_incentive_definitions",
                schema: "payroll");
        }
    }
}

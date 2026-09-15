using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Employee.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EmployeeCareerHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "leave_accrual_start_date",
                schema: "employee",
                table: "employees",
                type: "date",
                nullable: true);

            // Default "İzne esas hak ediş tarihi" = hire date for existing employees (same schema; career rows are
            // materialized lazily by the application, not here).
            migrationBuilder.Sql("UPDATE employee.employees SET leave_accrual_start_date = hire_date WHERE hire_date IS NOT NULL;");

            migrationBuilder.CreateTable(
                name: "career_records",
                schema: "employee",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    change_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    employment_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    manager_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_career_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_career_records_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "employee",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "career_org_assignments",
                schema: "employee",
                columns: table => new
                {
                    org_unit_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    career_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_unit_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_career_org_assignments", x => new { x.career_record_id, x.org_unit_type_id });
                    table.ForeignKey(
                        name: "fk_career_org_assignments_career_records_career_record_id",
                        column: x => x.career_record_id,
                        principalSchema: "employee",
                        principalTable: "career_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_career_org_assignments_org_unit_id",
                schema: "employee",
                table: "career_org_assignments",
                column: "org_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_career_records_employee_id_valid_from",
                schema: "employee",
                table: "career_records",
                columns: new[] { "employee_id", "valid_from" });

            migrationBuilder.CreateIndex(
                name: "ix_career_records_manager_employee_id",
                schema: "employee",
                table: "career_records",
                column: "manager_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_career_records_tenant_id",
                schema: "employee",
                table: "career_records",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "career_org_assignments",
                schema: "employee");

            migrationBuilder.DropTable(
                name: "career_records",
                schema: "employee");

            migrationBuilder.DropColumn(
                name: "leave_accrual_start_date",
                schema: "employee",
                table: "employees");
        }
    }
}

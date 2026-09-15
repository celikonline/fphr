using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Employee.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EmployeeProfileAndManager : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "display_name",
                schema: "employee",
                table: "employees",
                type: "character varying(201)",
                maxLength: 201,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AddColumn<DateOnly>(
                name: "birth_date",
                schema: "employee",
                table: "employees",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "first_name",
                schema: "employee",
                table: "employees",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "hire_date",
                schema: "employee",
                table: "employees",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_name",
                schema: "employee",
                table: "employees",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "manager_employee_id",
                schema: "employee",
                table: "employees",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "national_id",
                schema: "employee",
                table: "employees",
                type: "character varying(11)",
                maxLength: 11,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "personal_email",
                schema: "employee",
                table: "employees",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "work_email",
                schema: "employee",
                table: "employees",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "work_phone",
                schema: "employee",
                table: "employees",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            // Backfill names of pre-existing stub rows from display_name (last word = last name).
            migrationBuilder.Sql(
                """
                UPDATE employee.employees
                SET first_name = CASE WHEN position(' ' IN btrim(display_name)) > 0
                                      THEN left(regexp_replace(btrim(display_name), '\s+\S+$', ''), 100)
                                      ELSE left(btrim(display_name), 100) END,
                    last_name  = CASE WHEN position(' ' IN btrim(display_name)) > 0
                                      THEN left(substring(btrim(display_name) FROM '(\S+)$'), 100)
                                      ELSE '' END
                WHERE first_name = '';
                """);

            migrationBuilder.CreateIndex(
                name: "ix_employees_manager_employee_id",
                schema: "employee",
                table: "employees",
                column: "manager_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_employees_tenant_id_national_id",
                schema: "employee",
                table: "employees",
                columns: new[] { "tenant_id", "national_id" },
                unique: true,
                filter: "national_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_employees_manager_employee_id",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropIndex(
                name: "ix_employees_tenant_id_national_id",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "birth_date",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "first_name",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "hire_date",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "last_name",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "manager_employee_id",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "national_id",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "personal_email",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "work_email",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "work_phone",
                schema: "employee",
                table: "employees");

            migrationBuilder.AlterColumn<string>(
                name: "display_name",
                schema: "employee",
                table: "employees",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(201)",
                oldMaxLength: 201);
        }
    }
}

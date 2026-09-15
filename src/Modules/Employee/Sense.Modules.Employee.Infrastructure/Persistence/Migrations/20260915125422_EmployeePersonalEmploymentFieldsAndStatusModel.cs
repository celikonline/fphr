using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Employee.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EmployeePersonalEmploymentFieldsAndStatusModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Status model rename (Draft/Active/Suspended/Terminated replaces Active/Inactive/OnLeave): Active stays
            // Active, OnLeave folds into Active (no separate "on leave" concept until the Leave module exists),
            // Inactive becomes Terminated. Existing rows never had a hire date column to check, so none become Draft.
            migrationBuilder.Sql("UPDATE employee.employees SET status = 'Active' WHERE status = 'OnLeave';");
            migrationBuilder.Sql("UPDATE employee.employees SET status = 'Terminated' WHERE status = 'Inactive';");

            migrationBuilder.AddColumn<string>(
                name: "address_line",
                schema: "employee",
                table: "employees",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "blood_type",
                schema: "employee",
                table: "employees",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "child_count",
                schema: "employee",
                table: "employees",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "city",
                schema: "employee",
                table: "employees",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contract_type",
                schema: "employee",
                table: "employees",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "disability_degree",
                schema: "employee",
                table: "employees",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "education_status",
                schema: "employee",
                table: "employees",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "employment_type",
                schema: "employee",
                table: "employees",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gender",
                schema: "employee",
                table: "employees",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Unspecified");

            migrationBuilder.AddColumn<string>(
                name: "highest_education_level",
                schema: "employee",
                table: "employees",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "home_phone",
                schema: "employee",
                table: "employees",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "marital_status",
                schema: "employee",
                table: "employees",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Unspecified");

            migrationBuilder.AddColumn<string>(
                name: "military_status",
                schema: "employee",
                table: "employees",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mobile_phone",
                schema: "employee",
                table: "employees",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "postal_code",
                schema: "employee",
                table: "employees",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "suspended_at_utc",
                schema: "employee",
                table: "employees",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "suspension_reason",
                schema: "employee",
                table: "employees",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "address_line",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "blood_type",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "child_count",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "city",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "contract_type",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "disability_degree",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "education_status",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "employment_type",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "gender",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "highest_education_level",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "home_phone",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "marital_status",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "military_status",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "mobile_phone",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "postal_code",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "suspended_at_utc",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "suspension_reason",
                schema: "employee",
                table: "employees");

            // Best-effort reverse (the OnLeave/Active distinction folded into Active above cannot be recovered).
            migrationBuilder.Sql("UPDATE employee.employees SET status = 'Inactive' WHERE status = 'Terminated';");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Employee.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EmployeeTerminationWizard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_employer_representative",
                schema: "employee",
                table: "employees",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_underground_worker",
                schema: "employee",
                table: "employees",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "sgk_exit_code",
                schema: "employee",
                table: "employees",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "termination_date",
                schema: "employee",
                table: "employees",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "termination_reason_id",
                schema: "employee",
                table: "employees",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "termination_type",
                schema: "employee",
                table: "employees",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "termination_reasons",
                schema: "employee",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    default_sgk_exit_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    default_termination_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_termination_reasons", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_employees_termination_reason_id",
                schema: "employee",
                table: "employees",
                column: "termination_reason_id");

            migrationBuilder.CreateIndex(
                name: "ix_termination_reasons_tenant_id",
                schema: "employee",
                table: "termination_reasons",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_termination_reasons_tenant_id_code",
                schema: "employee",
                table: "termination_reasons",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_termination_reasons_tenant_id_is_active",
                schema: "employee",
                table: "termination_reasons",
                columns: new[] { "tenant_id", "is_active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "termination_reasons",
                schema: "employee");

            migrationBuilder.DropIndex(
                name: "ix_employees_termination_reason_id",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "is_employer_representative",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "is_underground_worker",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "sgk_exit_code",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "termination_date",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "termination_reason_id",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "termination_type",
                schema: "employee",
                table: "employees");
        }
    }
}

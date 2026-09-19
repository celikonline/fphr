using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Employee.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeMentor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "mentor_employee_id",
                schema: "employee",
                table: "employees",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_employees_mentor_employee_id",
                schema: "employee",
                table: "employees",
                column: "mentor_employee_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_employees_mentor_employee_id",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "mentor_employee_id",
                schema: "employee",
                table: "employees");
        }
    }
}

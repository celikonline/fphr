using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Employee.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeCostCenter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "cost_center_id",
                schema: "employee",
                table: "employees",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_employees_tenant_id_cost_center_id",
                schema: "employee",
                table: "employees",
                columns: new[] { "tenant_id", "cost_center_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_employees_tenant_id_cost_center_id",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "cost_center_id",
                schema: "employee",
                table: "employees");
        }
    }
}

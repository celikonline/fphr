using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Employee.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EmployeeSicilNumarasiAndNationality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "foreign_id_number",
                schema: "employee",
                table: "employees",
                type: "character varying(11)",
                maxLength: 11,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "nationality",
                schema: "employee",
                table: "employees",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Turkish");

            migrationBuilder.AddColumn<string>(
                name: "passport_number",
                schema: "employee",
                table: "employees",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sgk_occupation_code",
                schema: "employee",
                table: "employees",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sicil_numarasi",
                schema: "employee",
                table: "employees",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_employees_tenant_id_sicil_numarasi",
                schema: "employee",
                table: "employees",
                columns: new[] { "tenant_id", "sicil_numarasi" },
                unique: true,
                filter: "sicil_numarasi IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_employees_tenant_id_sicil_numarasi",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "foreign_id_number",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "nationality",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "passport_number",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "sgk_occupation_code",
                schema: "employee",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "sicil_numarasi",
                schema: "employee",
                table: "employees");
        }
    }
}

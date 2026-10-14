using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Employee.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonnelNumberManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "legacy_personnel_numbers",
                schema: "employee",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_system = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    personnel_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_legacy_personnel_numbers", x => x.id);
                    table.ForeignKey(
                        name: "fk_legacy_personnel_numbers_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "employee",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "personnel_number_ranges",
                schema: "employee",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    start_number = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    end_number = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    assignment_mode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    last_assigned_number = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_personnel_number_ranges", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "personnel_number_rules",
                schema: "employee",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    personnel_area_id = table.Column<Guid>(type: "uuid", nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    range_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_personnel_number_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_personnel_number_rules_personnel_number_ranges_range_id",
                        column: x => x.range_id,
                        principalSchema: "employee",
                        principalTable: "personnel_number_ranges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_legacy_personnel_numbers_employee_id",
                schema: "employee",
                table: "legacy_personnel_numbers",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_legacy_personnel_numbers_tenant_id",
                schema: "employee",
                table: "legacy_personnel_numbers",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_legacy_personnel_numbers_tenant_id_source_system_personnel_",
                schema: "employee",
                table: "legacy_personnel_numbers",
                columns: new[] { "tenant_id", "source_system", "personnel_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_personnel_number_ranges_tenant_id",
                schema: "employee",
                table: "personnel_number_ranges",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_personnel_number_ranges_tenant_id_code",
                schema: "employee",
                table: "personnel_number_ranges",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_personnel_number_rules_range_id",
                schema: "employee",
                table: "personnel_number_rules",
                column: "range_id");

            migrationBuilder.CreateIndex(
                name: "ix_personnel_number_rules_tenant_id",
                schema: "employee",
                table: "personnel_number_rules",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_personnel_number_rules_tenant_id_company_id_operation",
                schema: "employee",
                table: "personnel_number_rules",
                columns: new[] { "tenant_id", "company_id", "operation" },
                unique: true,
                filter: "is_active AND personnel_area_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_personnel_number_rules_tenant_id_company_id_operation_perso",
                schema: "employee",
                table: "personnel_number_rules",
                columns: new[] { "tenant_id", "company_id", "operation", "personnel_area_id" },
                unique: true,
                filter: "is_active AND personnel_area_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "legacy_personnel_numbers",
                schema: "employee");

            migrationBuilder.DropTable(
                name: "personnel_number_rules",
                schema: "employee");

            migrationBuilder.DropTable(
                name: "personnel_number_ranges",
                schema: "employee");
        }
    }
}

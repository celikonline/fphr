using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Employee.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "custom_field_definitions",
                schema: "employee",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    data_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    section = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    options = table.Column<string>(type: "jsonb", nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    is_employee_editable = table.Column<bool>(type: "boolean", nullable: false),
                    employee_edit_requires_approval = table.Column<bool>(type: "boolean", nullable: false),
                    is_reportable = table.Column<bool>(type: "boolean", nullable: false),
                    is_sensitive = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_custom_field_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "custom_field_values",
                schema: "employee",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_custom_field_values", x => x.id);
                    table.ForeignKey(
                        name: "fk_custom_field_values_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "employee",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_custom_field_definitions_tenant_id",
                schema: "employee",
                table: "custom_field_definitions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_custom_field_definitions_tenant_id_is_active",
                schema: "employee",
                table: "custom_field_definitions",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_custom_field_definitions_tenant_id_key",
                schema: "employee",
                table: "custom_field_definitions",
                columns: new[] { "tenant_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_custom_field_values_definition_id",
                schema: "employee",
                table: "custom_field_values",
                column: "definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_custom_field_values_employee_id",
                schema: "employee",
                table: "custom_field_values",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_custom_field_values_employee_id_definition_id",
                schema: "employee",
                table: "custom_field_values",
                columns: new[] { "employee_id", "definition_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_custom_field_values_tenant_id",
                schema: "employee",
                table: "custom_field_values",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "custom_field_definitions",
                schema: "employee");

            migrationBuilder.DropTable(
                name: "custom_field_values",
                schema: "employee");
        }
    }
}

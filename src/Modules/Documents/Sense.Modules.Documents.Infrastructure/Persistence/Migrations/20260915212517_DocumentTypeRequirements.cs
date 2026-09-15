using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Documents.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DocumentTypeRequirements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_type_definitions",
                schema: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    stage = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    condition = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    renewal_period_days = table.Column<int>(type: "integer", nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    is_sensitive = table.Column<bool>(type: "boolean", nullable: false),
                    default_retention_days = table.Column<int>(type: "integer", nullable: true),
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
                    table.PrimaryKey("pk_document_type_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employee_document_requirements",
                schema: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    waived_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    waived_by = table.Column<Guid>(type: "uuid", nullable: true),
                    waived_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    evaluated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_document_requirements", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_document_type_definitions_tenant_id",
                schema: "documents",
                table: "document_type_definitions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_type_definitions_tenant_id_code",
                schema: "documents",
                table: "document_type_definitions",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_document_type_definitions_tenant_id_stage",
                schema: "documents",
                table: "document_type_definitions",
                columns: new[] { "tenant_id", "stage" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_document_requirements_tenant_id",
                schema: "documents",
                table: "employee_document_requirements",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_document_requirements_tenant_id_employee_id_docume",
                schema: "documents",
                table: "employee_document_requirements",
                columns: new[] { "tenant_id", "employee_id", "document_type_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employee_document_requirements_tenant_id_status",
                schema: "documents",
                table: "employee_document_requirements",
                columns: new[] { "tenant_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_type_definitions",
                schema: "documents");

            migrationBuilder.DropTable(
                name: "employee_document_requirements",
                schema: "documents");
        }
    }
}

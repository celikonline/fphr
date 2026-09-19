using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Documents.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVisaSettingsAndDocumentDeliveryFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "employee_id",
                schema: "documents",
                table: "documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "visa_document_request_id",
                schema: "documents",
                table: "documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "visa_settings",
                schema: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    approver_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    template_document_ids = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_visa_settings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_visa_document_processes_tenant_id_name",
                schema: "documents",
                table: "visa_document_processes",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_documents_tenant_id_employee_id",
                schema: "documents",
                table: "documents",
                columns: new[] { "tenant_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_documents_visa_document_request_id",
                schema: "documents",
                table: "documents",
                column: "visa_document_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_visa_settings_tenant_id",
                schema: "documents",
                table: "visa_settings",
                column: "tenant_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "visa_settings",
                schema: "documents");

            migrationBuilder.DropIndex(
                name: "ix_visa_document_processes_tenant_id_name",
                schema: "documents",
                table: "visa_document_processes");

            migrationBuilder.DropIndex(
                name: "ix_documents_tenant_id_employee_id",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropIndex(
                name: "ix_documents_visa_document_request_id",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "employee_id",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "visa_document_request_id",
                schema: "documents",
                table: "documents");
        }
    }
}

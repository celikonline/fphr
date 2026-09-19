using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Documents.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds `shared_company_files` (senseik-028). Also creates `visa_document_processes` /
    /// `visa_document_requests`: those entities' EF configuration (VisaDocumentProcessConfiguration /
    /// VisaDocumentRequestConfiguration) was already wired into DocumentsDbContext.OnModelCreating by an earlier,
    /// still-in-progress commit (senseik-001, see docs/handoffs/visa-document-request.md "Migration üretilmedi —
    /// tablolar DB'de yok") without ever generating a migration for them, so the model snapshot was already out of
    /// sync with migration history before this change. `dotnet ef migrations add` always diffs the live model
    /// against the snapshot, so any migration generated for this module from now on would pick up that same gap;
    /// leaving it out here would either reproduce the same problem for the next migration or (worse) require hand-
    /// editing the snapshot to hide it, which would make the model snapshot silently lie about what's actually in
    /// the database. Letting EF fix it here is additive-only (CREATE TABLE for tables that were never created) and
    /// unblocks senseik-001 without touching its application/API/web layers, which remain untouched.
    /// </summary>
    public partial class AddSharedCompanyFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shared_company_files",
                schema: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    file_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    e_tag = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    visibility = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    allowed_roles = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shared_company_files", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "visa_document_processes",
                schema: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    country_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    modified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_visa_document_processes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "visa_document_requests",
                schema: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    visa_document_process_id = table.Column<Guid>(type: "uuid", nullable: true),
                    travel_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    travel_end = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    country_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    visa_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    rejected_by = table.Column<Guid>(type: "uuid", nullable: true),
                    rejected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    approved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_visa_document_requests", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shared_company_files_tenant_id",
                schema: "documents",
                table: "shared_company_files",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_shared_company_files_tenant_id_file_key",
                schema: "documents",
                table: "shared_company_files",
                columns: new[] { "tenant_id", "file_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shared_company_files_tenant_id_status",
                schema: "documents",
                table: "shared_company_files",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_visa_document_processes_tenant_id",
                schema: "documents",
                table: "visa_document_processes",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_visa_document_processes_tenant_id_country_code",
                schema: "documents",
                table: "visa_document_processes",
                columns: new[] { "tenant_id", "country_code" });

            migrationBuilder.CreateIndex(
                name: "ix_visa_document_processes_tenant_id_is_active",
                schema: "documents",
                table: "visa_document_processes",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_visa_document_requests_tenant_id",
                schema: "documents",
                table: "visa_document_requests",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_visa_document_requests_tenant_id_approval_request_id",
                schema: "documents",
                table: "visa_document_requests",
                columns: new[] { "tenant_id", "approval_request_id" });

            migrationBuilder.CreateIndex(
                name: "ix_visa_document_requests_tenant_id_employee_id",
                schema: "documents",
                table: "visa_document_requests",
                columns: new[] { "tenant_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_visa_document_requests_tenant_id_status",
                schema: "documents",
                table: "visa_document_requests",
                columns: new[] { "tenant_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shared_company_files",
                schema: "documents");

            migrationBuilder.DropTable(
                name: "visa_document_processes",
                schema: "documents");

            migrationBuilder.DropTable(
                name: "visa_document_requests",
                schema: "documents");
        }
    }
}

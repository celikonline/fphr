using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Leave.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LeaveRequestAttachment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "leave_request_attachments",
                schema: "leave",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    attached_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attached_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_request_attachments", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_leave_request_attachments_tenant_id",
                schema: "leave",
                table: "leave_request_attachments",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_request_attachments_tenant_id_leave_request_id",
                schema: "leave",
                table: "leave_request_attachments",
                columns: new[] { "tenant_id", "leave_request_id" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_request_attachments_tenant_id_leave_request_id_docume",
                schema: "leave",
                table: "leave_request_attachments",
                columns: new[] { "tenant_id", "leave_request_id", "document_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "leave_request_attachments",
                schema: "leave");
        }
    }
}

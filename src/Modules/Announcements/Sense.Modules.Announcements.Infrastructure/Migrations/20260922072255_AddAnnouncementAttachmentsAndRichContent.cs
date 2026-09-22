using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Announcements.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAnnouncementAttachmentsAndRichContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "content",
                schema: "announcements",
                table: "announcements",
                type: "character varying(50000)",
                maxLength: 50000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(10000)",
                oldMaxLength: 10000);

            migrationBuilder.CreateTable(
                name: "announcement_attachments",
                schema: "announcements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    announcement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    content_type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_announcement_attachments", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_announcement_attachments_tenant_id",
                schema: "announcements",
                table: "announcement_attachments",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_announcement_attachments_tenant_id_announcement_id",
                schema: "announcements",
                table: "announcement_attachments",
                columns: new[] { "tenant_id", "announcement_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "announcement_attachments",
                schema: "announcements");

            migrationBuilder.AlterColumn<string>(
                name: "content",
                schema: "announcements",
                table: "announcements",
                type: "character varying(10000)",
                maxLength: 10000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50000)",
                oldMaxLength: 50000);
        }
    }
}

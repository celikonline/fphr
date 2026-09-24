using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Announcements.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AnnouncementScheduledFor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "scheduled_for",
                schema: "announcements",
                table: "announcements",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_announcements_tenant_id_status_scheduled_for",
                schema: "announcements",
                table: "announcements",
                columns: new[] { "tenant_id", "status", "scheduled_for" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_announcements_tenant_id_status_scheduled_for",
                schema: "announcements",
                table: "announcements");

            migrationBuilder.DropColumn(
                name: "scheduled_for",
                schema: "announcements",
                table: "announcements");
        }
    }
}

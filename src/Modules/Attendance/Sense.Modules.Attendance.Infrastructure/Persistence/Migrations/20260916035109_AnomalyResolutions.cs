using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Attendance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AnomalyResolutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "anomaly_resolutions",
                schema: "attendance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    attendance_day_id = table.Column<Guid>(type: "uuid", nullable: false),
                    anomaly = table.Column<string>(type: "text", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    linked_entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    linked_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_anomaly_resolutions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_anomaly_resolutions_tenant_id",
                schema: "attendance",
                table: "anomaly_resolutions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_anomaly_resolutions_tenant_id_attendance_day_id",
                schema: "attendance",
                table: "anomaly_resolutions",
                columns: new[] { "tenant_id", "attendance_day_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "anomaly_resolutions",
                schema: "attendance");
        }
    }
}

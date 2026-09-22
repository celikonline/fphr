using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.OccupationalSafety.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOhsIncidents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "incidents",
                schema: "ohs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    event_date = table.Column<DateOnly>(type: "date", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reported_by_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    sgk_notification_deadline = table.Column<DateOnly>(type: "date", nullable: true),
                    sgk_notified_on = table.Column<DateOnly>(type: "date", nullable: true),
                    corrective_action = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    closed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incidents", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_incidents_tenant_id",
                schema: "ohs",
                table: "incidents",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_incidents_tenant_id_event_date",
                schema: "ohs",
                table: "incidents",
                columns: new[] { "tenant_id", "event_date" });

            migrationBuilder.CreateIndex(
                name: "ix_incidents_tenant_id_reported_by_employee_id",
                schema: "ohs",
                table: "incidents",
                columns: new[] { "tenant_id", "reported_by_employee_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "incidents",
                schema: "ohs");
        }
    }
}

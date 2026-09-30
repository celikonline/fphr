using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Announcements.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnrichCompanyEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "category_id",
                schema: "announcements",
                table: "company_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ends_at",
                schema: "announcements",
                table: "company_events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_all_day",
                schema: "announcements",
                table: "company_events",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "location",
                schema: "announcements",
                table: "company_events",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mode",
                schema: "announcements",
                table: "company_events",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "notify_invitees",
                schema: "announcements",
                table: "company_events",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "online_url",
                schema: "announcements",
                table: "company_events",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "starts_at",
                schema: "announcements",
                table: "company_events",
                type: "timestamp with time zone",
                nullable: true);

            // Legacy rows (date-only EventDate + enum type) become all-day events on the same date; mode stays null.
            migrationBuilder.Sql(
                "UPDATE announcements.company_events SET starts_at = (event_date::timestamp AT TIME ZONE 'UTC'), is_all_day = TRUE WHERE starts_at IS NULL;");

            migrationBuilder.CreateTable(
                name: "company_event_categories",
                schema: "announcements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_event_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "company_event_invitees",
                schema: "announcements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_event_invitees", x => x.id);
                    table.ForeignKey(
                        name: "fk_company_event_invitees_company_events_company_event_id",
                        column: x => x.company_event_id,
                        principalSchema: "announcements",
                        principalTable: "company_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_company_events_tenant_id_category_id",
                schema: "announcements",
                table: "company_events",
                columns: new[] { "tenant_id", "category_id" });

            migrationBuilder.CreateIndex(
                name: "ix_company_events_tenant_id_starts_at",
                schema: "announcements",
                table: "company_events",
                columns: new[] { "tenant_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_company_event_categories_tenant_id",
                schema: "announcements",
                table: "company_event_categories",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_company_event_categories_tenant_id_name",
                schema: "announcements",
                table: "company_event_categories",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_company_event_invitees_company_event_id",
                schema: "announcements",
                table: "company_event_invitees",
                column: "company_event_id");

            migrationBuilder.CreateIndex(
                name: "ix_company_event_invitees_tenant_id",
                schema: "announcements",
                table: "company_event_invitees",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_company_event_invitees_tenant_id_company_event_id_employee_",
                schema: "announcements",
                table: "company_event_invitees",
                columns: new[] { "tenant_id", "company_event_id", "employee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_company_event_invitees_tenant_id_employee_id",
                schema: "announcements",
                table: "company_event_invitees",
                columns: new[] { "tenant_id", "employee_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company_event_categories",
                schema: "announcements");

            migrationBuilder.DropTable(
                name: "company_event_invitees",
                schema: "announcements");

            migrationBuilder.DropIndex(
                name: "ix_company_events_tenant_id_category_id",
                schema: "announcements",
                table: "company_events");

            migrationBuilder.DropIndex(
                name: "ix_company_events_tenant_id_starts_at",
                schema: "announcements",
                table: "company_events");

            migrationBuilder.DropColumn(
                name: "category_id",
                schema: "announcements",
                table: "company_events");

            migrationBuilder.DropColumn(
                name: "ends_at",
                schema: "announcements",
                table: "company_events");

            migrationBuilder.DropColumn(
                name: "is_all_day",
                schema: "announcements",
                table: "company_events");

            migrationBuilder.DropColumn(
                name: "location",
                schema: "announcements",
                table: "company_events");

            migrationBuilder.DropColumn(
                name: "mode",
                schema: "announcements",
                table: "company_events");

            migrationBuilder.DropColumn(
                name: "notify_invitees",
                schema: "announcements",
                table: "company_events");

            migrationBuilder.DropColumn(
                name: "online_url",
                schema: "announcements",
                table: "company_events");

            migrationBuilder.DropColumn(
                name: "starts_at",
                schema: "announcements",
                table: "company_events");
        }
    }
}

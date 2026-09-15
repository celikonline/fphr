using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Organization.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHolidayCalendars : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "holiday_calendars",
                schema: "org",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    country_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    is_system_defined = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holiday_calendars", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "holiday_calendar_entries",
                schema: "org",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    holiday_calendar_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_recurring_annually = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holiday_calendar_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_holiday_calendar_entries_holiday_calendars_holiday_calendar",
                        column: x => x.holiday_calendar_id,
                        principalSchema: "org",
                        principalTable: "holiday_calendars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_companies_holiday_calendar_id",
                schema: "org",
                table: "companies",
                column: "holiday_calendar_id");

            migrationBuilder.CreateIndex(
                name: "ix_holiday_calendar_entries_holiday_calendar_id",
                schema: "org",
                table: "holiday_calendar_entries",
                column: "holiday_calendar_id");

            migrationBuilder.CreateIndex(
                name: "ix_holiday_calendar_entries_tenant_id",
                schema: "org",
                table: "holiday_calendar_entries",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_holiday_calendar_entries_tenant_id_holiday_calendar_id",
                schema: "org",
                table: "holiday_calendar_entries",
                columns: new[] { "tenant_id", "holiday_calendar_id" });

            migrationBuilder.CreateIndex(
                name: "ix_holiday_calendars_tenant_id",
                schema: "org",
                table: "holiday_calendars",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_holiday_calendars_tenant_id_name",
                schema: "org",
                table: "holiday_calendars",
                columns: new[] { "tenant_id", "name" });

            migrationBuilder.AddForeignKey(
                name: "fk_companies_holiday_calendars_holiday_calendar_id",
                schema: "org",
                table: "companies",
                column: "holiday_calendar_id",
                principalSchema: "org",
                principalTable: "holiday_calendars",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_companies_holiday_calendars_holiday_calendar_id",
                schema: "org",
                table: "companies");

            migrationBuilder.DropTable(
                name: "holiday_calendar_entries",
                schema: "org");

            migrationBuilder.DropTable(
                name: "holiday_calendars",
                schema: "org");

            migrationBuilder.DropIndex(
                name: "ix_companies_holiday_calendar_id",
                schema: "org",
                table: "companies");
        }
    }
}

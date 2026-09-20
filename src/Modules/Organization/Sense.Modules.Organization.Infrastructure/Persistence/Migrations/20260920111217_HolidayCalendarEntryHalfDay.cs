using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Organization.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HolidayCalendarEntryHalfDay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_half_day",
                schema: "org",
                table: "holiday_calendar_entries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Existing tenants: the seeded system calendar only carried "yarım gün" in the arife names (audit A4);
            // flag those rows so leave counting deducts 0.5 day. Custom calendars are left to their owners.
            migrationBuilder.Sql(
                """
                UPDATE org.holiday_calendar_entries e
                SET is_half_day = TRUE
                FROM org.holiday_calendars c
                WHERE c.id = e.holiday_calendar_id
                  AND c.is_system_defined
                  AND e.name LIKE '%yarım gün%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_half_day",
                schema: "org",
                table: "holiday_calendar_entries");
        }
    }
}

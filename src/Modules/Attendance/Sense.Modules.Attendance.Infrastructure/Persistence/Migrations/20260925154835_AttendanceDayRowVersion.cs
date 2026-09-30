using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Attendance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AttendanceDayRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "attendance",
                table: "attendance_days",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "attendance",
                table: "attendance_days");
        }
    }
}

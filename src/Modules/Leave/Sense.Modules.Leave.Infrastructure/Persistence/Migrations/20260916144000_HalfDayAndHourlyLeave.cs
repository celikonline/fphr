using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Leave.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HalfDayAndHourlyLeave : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeOnly>(
                name: "end_time",
                schema: "leave",
                table: "leave_requests",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "half_day_period",
                schema: "leave",
                table: "leave_requests",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "start_time",
                schema: "leave",
                table: "leave_requests",
                type: "time without time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "end_time",
                schema: "leave",
                table: "leave_requests");

            migrationBuilder.DropColumn(
                name: "half_day_period",
                schema: "leave",
                table: "leave_requests");

            migrationBuilder.DropColumn(
                name: "start_time",
                schema: "leave",
                table: "leave_requests");
        }
    }
}

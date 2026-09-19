using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Leave.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F05_OvertimePayRateAndExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing rows predate the rate selection (kart senseik-012 §1) - backfilled to Rate50 (m.41's default
            // "asıl fazla mesai" rate, the same default OvertimeRequest.Submit uses for a caller that omits it).
            migrationBuilder.AddColumn<string>(
                name: "pay_rate_kind",
                schema: "leave",
                table: "overtime_requests",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Rate50");

            migrationBuilder.AddColumn<decimal>(
                name: "pay_rate_multiplier",
                schema: "leave",
                table: "overtime_requests",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 1.5m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "used_by_date",
                schema: "leave",
                table: "overtime_conversions",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pay_rate_kind",
                schema: "leave",
                table: "overtime_requests");

            migrationBuilder.DropColumn(
                name: "pay_rate_multiplier",
                schema: "leave",
                table: "overtime_requests");

            migrationBuilder.DropColumn(
                name: "used_by_date",
                schema: "leave",
                table: "overtime_conversions");
        }
    }
}

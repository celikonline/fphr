using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Leave.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LeaveManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cancellation_reason",
                schema: "leave",
                table: "leave_requests",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "cancelled_at",
                schema: "leave",
                table: "leave_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cancelled_by_user_id",
                schema: "leave",
                table: "leave_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "previous_request_id",
                schema: "leave",
                table: "leave_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_leave_requests_tenant_id_start_date_end_date",
                schema: "leave",
                table: "leave_requests",
                columns: new[] { "tenant_id", "start_date", "end_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_leave_requests_tenant_id_start_date_end_date",
                schema: "leave",
                table: "leave_requests");

            migrationBuilder.DropColumn(
                name: "cancellation_reason",
                schema: "leave",
                table: "leave_requests");

            migrationBuilder.DropColumn(
                name: "cancelled_at",
                schema: "leave",
                table: "leave_requests");

            migrationBuilder.DropColumn(
                name: "cancelled_by_user_id",
                schema: "leave",
                table: "leave_requests");

            migrationBuilder.DropColumn(
                name: "previous_request_id",
                schema: "leave",
                table: "leave_requests");
        }
    }
}

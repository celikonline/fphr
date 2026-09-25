using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Payroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayslipAcknowledgementAndObjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "acknowledged_at",
                schema: "payroll",
                table: "payslips",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "acknowledged_from_ip",
                schema: "payroll",
                table: "payslips",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "acknowledged_user_agent",
                schema: "payroll",
                table: "payslips",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "acknowledgement_history",
                schema: "payroll",
                table: "payslips",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "objected_at",
                schema: "payroll",
                table: "payslips",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "objection_approval_request_id",
                schema: "payroll",
                table: "payslips",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "objection_decided_at",
                schema: "payroll",
                table: "payslips",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "objection_reason",
                schema: "payroll",
                table: "payslips",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "objection_status",
                schema: "payroll",
                table: "payslips",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "published_at",
                schema: "payroll",
                table: "payslips",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "acknowledged_at",
                schema: "payroll",
                table: "payslips");

            migrationBuilder.DropColumn(
                name: "acknowledged_from_ip",
                schema: "payroll",
                table: "payslips");

            migrationBuilder.DropColumn(
                name: "acknowledged_user_agent",
                schema: "payroll",
                table: "payslips");

            migrationBuilder.DropColumn(
                name: "acknowledgement_history",
                schema: "payroll",
                table: "payslips");

            migrationBuilder.DropColumn(
                name: "objected_at",
                schema: "payroll",
                table: "payslips");

            migrationBuilder.DropColumn(
                name: "objection_approval_request_id",
                schema: "payroll",
                table: "payslips");

            migrationBuilder.DropColumn(
                name: "objection_decided_at",
                schema: "payroll",
                table: "payslips");

            migrationBuilder.DropColumn(
                name: "objection_reason",
                schema: "payroll",
                table: "payslips");

            migrationBuilder.DropColumn(
                name: "objection_status",
                schema: "payroll",
                table: "payslips");

            migrationBuilder.DropColumn(
                name: "published_at",
                schema: "payroll",
                table: "payslips");
        }
    }
}

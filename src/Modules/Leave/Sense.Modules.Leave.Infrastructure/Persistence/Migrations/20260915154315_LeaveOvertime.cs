using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Leave.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LeaveOvertime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "overtime_consents",
                schema: "leave",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    given_at = table.Column<DateOnly>(type: "date", nullable: false),
                    source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    covers_night_work = table.Column<bool>(type: "boolean", nullable: false),
                    revocation_notice_at = table.Column<DateOnly>(type: "date", nullable: true),
                    revocation_effective_at = table.Column<DateOnly>(type: "date", nullable: true),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_overtime_consents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "overtime_conversions",
                schema: "leave",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    overtime_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    minutes_basis = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    rate_snapshot = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    basis_minutes = table.Column<int>(type: "integer", nullable: false),
                    result_minutes = table.Column<int>(type: "integer", nullable: false),
                    result_days = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    daily_work_minutes_snapshot = table.Column<int>(type: "integer", nullable: false),
                    leave_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ledger_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    converted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    converted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_overtime_conversions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "overtime_requests",
                schema: "leave",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_date = table.Column<DateOnly>(type: "date", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    day_kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    overlap_with_schedule_minutes = table.Column<int>(type: "integer", nullable: false),
                    payable_minutes = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    submitted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    submitted_ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    decided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    conversion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_overtime_requests", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_overtime_consents_tenant_id",
                schema: "leave",
                table: "overtime_consents",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_overtime_consents_tenant_id_employee_id",
                schema: "leave",
                table: "overtime_consents",
                columns: new[] { "tenant_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_overtime_conversions_overtime_request_id",
                schema: "leave",
                table: "overtime_conversions",
                column: "overtime_request_id",
                unique: true,
                filter: "status = 'Posted'");

            migrationBuilder.CreateIndex(
                name: "ix_overtime_conversions_tenant_id",
                schema: "leave",
                table: "overtime_conversions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_overtime_conversions_tenant_id_employee_id",
                schema: "leave",
                table: "overtime_conversions",
                columns: new[] { "tenant_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_overtime_requests_tenant_id",
                schema: "leave",
                table: "overtime_requests",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_overtime_requests_tenant_id_approval_request_id",
                schema: "leave",
                table: "overtime_requests",
                columns: new[] { "tenant_id", "approval_request_id" });

            migrationBuilder.CreateIndex(
                name: "ix_overtime_requests_tenant_id_employee_id_work_date",
                schema: "leave",
                table: "overtime_requests",
                columns: new[] { "tenant_id", "employee_id", "work_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "overtime_consents",
                schema: "leave");

            migrationBuilder.DropTable(
                name: "overtime_conversions",
                schema: "leave");

            migrationBuilder.DropTable(
                name: "overtime_requests",
                schema: "leave");
        }
    }
}

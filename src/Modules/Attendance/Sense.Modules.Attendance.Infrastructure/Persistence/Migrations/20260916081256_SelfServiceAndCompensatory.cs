using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Attendance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SelfServiceAndCompensatory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "compensatory_work_ledgers",
                schema: "attendance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    week_start = table.Column<DateOnly>(type: "date", nullable: false),
                    deficit_minutes = table.Column<int>(type: "integer", nullable: false),
                    compensated_minutes = table.Column<int>(type: "integer", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_compensatory_work_ledgers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employee_availabilities",
                schema: "attendance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    start = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    end = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    kind = table.Column<string>(type: "text", nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_availabilities", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_compensatory_work_ledgers_tenant_id",
                schema: "attendance",
                table: "compensatory_work_ledgers",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_compensatory_work_ledgers_tenant_id_employee_id_week_start",
                schema: "attendance",
                table: "compensatory_work_ledgers",
                columns: new[] { "tenant_id", "employee_id", "week_start" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employee_availabilities_tenant_id",
                schema: "attendance",
                table: "employee_availabilities",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_availabilities_tenant_id_employee_id_date",
                schema: "attendance",
                table: "employee_availabilities",
                columns: new[] { "tenant_id", "employee_id", "date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "compensatory_work_ledgers",
                schema: "attendance");

            migrationBuilder.DropTable(
                name: "employee_availabilities",
                schema: "attendance");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Payroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExitSettlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "exit_settlements",
                schema: "payroll",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    termination_date = table.Column<DateOnly>(type: "date", nullable: false),
                    cause_category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    seniority_days = table.Column<int>(type: "integer", nullable: false),
                    daily_gross_wage = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    severance_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    notice_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unused_leave_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    steps_json = table.Column<string>(type: "jsonb", maxLength: 500, nullable: false),
                    calculated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exit_settlements", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_exit_settlements_tenant_id",
                schema: "payroll",
                table: "exit_settlements",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_exit_settlements_tenant_id_employee_id_termination_date",
                schema: "payroll",
                table: "exit_settlements",
                columns: new[] { "tenant_id", "employee_id", "termination_date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exit_settlements",
                schema: "payroll");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Payroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExitSettlementLinesAndNet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "income_tax",
                schema: "payroll",
                table: "exit_settlements",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "net_calculated",
                schema: "payroll",
                table: "exit_settlements",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "net_total",
                schema: "payroll",
                table: "exit_settlements",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "sgk_deduction",
                schema: "payroll",
                table: "exit_settlements",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "stamp_tax",
                schema: "payroll",
                table: "exit_settlements",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "exit_settlement_lines",
                schema: "payroll",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    exit_settlement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    gross_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    sgk_subject = table.Column<bool>(type: "boolean", nullable: false),
                    income_tax_subject = table.Column<bool>(type: "boolean", nullable: false),
                    stamp_tax_subject = table.Column<bool>(type: "boolean", nullable: false),
                    is_automatic = table.Column<bool>(type: "boolean", nullable: false),
                    sgk_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    income_tax_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    stamp_tax_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    net_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exit_settlement_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_exit_settlement_lines_exit_settlements_exit_settlement_id",
                        column: x => x.exit_settlement_id,
                        principalSchema: "payroll",
                        principalTable: "exit_settlements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_exit_settlement_lines_exit_settlement_id",
                schema: "payroll",
                table: "exit_settlement_lines",
                column: "exit_settlement_id");

            migrationBuilder.CreateIndex(
                name: "ix_exit_settlement_lines_tenant_id",
                schema: "payroll",
                table: "exit_settlement_lines",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exit_settlement_lines",
                schema: "payroll");

            migrationBuilder.DropColumn(
                name: "income_tax",
                schema: "payroll",
                table: "exit_settlements");

            migrationBuilder.DropColumn(
                name: "net_calculated",
                schema: "payroll",
                table: "exit_settlements");

            migrationBuilder.DropColumn(
                name: "net_total",
                schema: "payroll",
                table: "exit_settlements");

            migrationBuilder.DropColumn(
                name: "sgk_deduction",
                schema: "payroll",
                table: "exit_settlements");

            migrationBuilder.DropColumn(
                name: "stamp_tax",
                schema: "payroll",
                table: "exit_settlements");
        }
    }
}

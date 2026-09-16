using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Expense.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PendingModelSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "paid_by_employee_id",
                schema: "expense",
                table: "expense_claims",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_note",
                schema: "expense",
                table: "expense_claims",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "paid_by_employee_id",
                schema: "expense",
                table: "advance_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_note",
                schema: "expense",
                table: "advance_requests",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "paid_by_employee_id",
                schema: "expense",
                table: "expense_claims");

            migrationBuilder.DropColumn(
                name: "payment_note",
                schema: "expense",
                table: "expense_claims");

            migrationBuilder.DropColumn(
                name: "paid_by_employee_id",
                schema: "expense",
                table: "advance_requests");

            migrationBuilder.DropColumn(
                name: "payment_note",
                schema: "expense",
                table: "advance_requests");
        }
    }
}

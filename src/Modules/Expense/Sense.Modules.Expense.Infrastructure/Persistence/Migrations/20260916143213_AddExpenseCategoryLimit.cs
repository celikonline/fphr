using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Expense.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseCategoryLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "max_amount",
                schema: "expense",
                table: "expense_categories",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "max_amount",
                schema: "expense",
                table: "expense_categories");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Expense.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseReceiptAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "expense_receipt_attachments",
                schema: "expense",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    expense_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expense_receipt_attachments", x => x.id);
                    table.ForeignKey(
                        name: "fk_expense_receipt_attachments_expense_items_expense_item_id",
                        column: x => x.expense_item_id,
                        principalSchema: "expense",
                        principalTable: "expense_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_expense_receipt_attachments_expense_item_id_document_id",
                schema: "expense",
                table: "expense_receipt_attachments",
                columns: new[] { "expense_item_id", "document_id" },
                unique: true);

            // Preserve the legacy scalar verbatim (including non-GUID storage keys); only real GUID-shaped
            // references get a link. File availability and access remain checked by the Documents contract.
            migrationBuilder.Sql("""
                INSERT INTO expense.expense_receipt_attachments
                    (id, expense_item_id, document_id, position, created_at, created_user_id)
                SELECT gen_random_uuid(), id, btrim(receipt_file_id, ' {}()')::uuid, 0,
                    created_at, created_user_id
                FROM expense.expense_items
                WHERE btrim(receipt_file_id, ' {}()') ~*
                    '^([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}|[0-9a-f]{32})$';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "expense_receipt_attachments",
                schema: "expense");
        }
    }
}

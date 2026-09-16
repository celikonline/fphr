using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Expense.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialExpense : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "expense");

            migrationBuilder.CreateTable(
                name: "advance_requests",
                schema: "expense",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    requested_date = table.Column<DateOnly>(type: "date", nullable: false),
                    is_installment_requested = table.Column<bool>(type: "boolean", nullable: false),
                    installment_count = table.Column<int>(type: "integer", nullable: true),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    paid_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_advance_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "expense_categories",
                schema: "expense",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    default_tax_rate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expense_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "expense_claims",
                schema: "expense",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    decided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    paid_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expense_claims", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "expense",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    handler = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_messages", x => new { x.message_id, x.handler });
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "expense",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload = table.Column<string>(type: "jsonb", maxLength: 500, nullable: false),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_dead = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "expense_items",
                schema: "expense",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    receipt_date = table.Column<DateOnly>(type: "date", nullable: false),
                    tax_rate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    tax_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    receipt_file_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expense_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_expense_items_expense_claims_claim_id",
                        column: x => x.claim_id,
                        principalSchema: "expense",
                        principalTable: "expense_claims",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_advance_requests_tenant_id",
                schema: "expense",
                table: "advance_requests",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_advance_requests_tenant_id_employee_id",
                schema: "expense",
                table: "advance_requests",
                columns: new[] { "tenant_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_advance_requests_tenant_id_status",
                schema: "expense",
                table: "advance_requests",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_expense_categories_tenant_id",
                schema: "expense",
                table: "expense_categories",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_expense_categories_tenant_id_code",
                schema: "expense",
                table: "expense_categories",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_expense_claims_tenant_id",
                schema: "expense",
                table: "expense_claims",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_expense_claims_tenant_id_employee_id",
                schema: "expense",
                table: "expense_claims",
                columns: new[] { "tenant_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_expense_claims_tenant_id_status",
                schema: "expense",
                table: "expense_claims",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_expense_items_claim_id",
                schema: "expense",
                table: "expense_items",
                column: "claim_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at_next_attempt_at",
                schema: "expense",
                table: "outbox_messages",
                columns: new[] { "processed_at", "next_attempt_at" },
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_tenant_id",
                schema: "expense",
                table: "outbox_messages",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "advance_requests",
                schema: "expense");

            migrationBuilder.DropTable(
                name: "expense_categories",
                schema: "expense");

            migrationBuilder.DropTable(
                name: "expense_items",
                schema: "expense");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "expense");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "expense");

            migrationBuilder.DropTable(
                name: "expense_claims",
                schema: "expense");
        }
    }
}

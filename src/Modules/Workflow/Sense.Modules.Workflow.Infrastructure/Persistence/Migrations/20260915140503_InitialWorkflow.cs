using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Workflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "workflow");

            migrationBuilder.CreateTable(
                name: "approval_policies",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    show_current_approver_to_requester = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approval_policies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "approval_requests",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requester_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: true),
                    policy_name_at_submission = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    resolved_from_org_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_by_inheritance = table.Column<bool>(type: "boolean", nullable: false),
                    show_current_approver_to_requester = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    current_step_number = table.Column<int>(type: "integer", nullable: true),
                    step_count = table.Column<int>(type: "integer", nullable: false),
                    submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    submitted_ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    submitted_user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approval_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "email_decision_tokens",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    approval_task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_decision_tokens", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "workflow",
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
                schema: "workflow",
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
                name: "approval_policy_steps",
                schema: "workflow",
                columns: table => new
                {
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_number = table.Column<int>(type: "integer", nullable: false),
                    approver_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approval_policy_steps", x => new { x.policy_id, x.step_number });
                    table.ForeignKey(
                        name: "fk_approval_policy_steps_approval_policies_policy_id",
                        column: x => x.policy_id,
                        principalSchema: "workflow",
                        principalTable: "approval_policies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "approval_history_entries",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    step_number = table.Column<int>(type: "integer", nullable: true),
                    actor_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    outcome = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    channel = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approval_history_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_approval_history_entries_approval_requests_approval_request",
                        column: x => x.approval_request_id,
                        principalSchema: "workflow",
                        principalTable: "approval_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "approval_tasks",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_number = table.Column<int>(type: "integer", nullable: false),
                    approver_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fallback_role_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    decided_by_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    was_override = table.Column<bool>(type: "boolean", nullable: false),
                    decision_channel = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    decision_ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    decision_user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approval_tasks", x => x.id);
                    table.ForeignKey(
                        name: "fk_approval_tasks_approval_requests_approval_request_id",
                        column: x => x.approval_request_id,
                        principalSchema: "workflow",
                        principalTable: "approval_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_approval_history_entries_approval_request_id",
                schema: "workflow",
                table: "approval_history_entries",
                column: "approval_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_approval_history_entries_tenant_id",
                schema: "workflow",
                table: "approval_history_entries",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_approval_history_entries_tenant_id_approval_request_id",
                schema: "workflow",
                table: "approval_history_entries",
                columns: new[] { "tenant_id", "approval_request_id" });

            migrationBuilder.CreateIndex(
                name: "ix_approval_policies_tenant_id",
                schema: "workflow",
                table: "approval_policies",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_approval_policies_tenant_id_request_type",
                schema: "workflow",
                table: "approval_policies",
                columns: new[] { "tenant_id", "request_type" });

            migrationBuilder.CreateIndex(
                name: "ix_approval_requests_tenant_id",
                schema: "workflow",
                table: "approval_requests",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_approval_requests_tenant_id_requester_employee_id",
                schema: "workflow",
                table: "approval_requests",
                columns: new[] { "tenant_id", "requester_employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_approval_requests_tenant_id_status",
                schema: "workflow",
                table: "approval_requests",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_approval_tasks_approval_request_id",
                schema: "workflow",
                table: "approval_tasks",
                column: "approval_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_approval_tasks_tenant_id",
                schema: "workflow",
                table: "approval_tasks",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_approval_tasks_tenant_id_approver_employee_id",
                schema: "workflow",
                table: "approval_tasks",
                columns: new[] { "tenant_id", "approver_employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_approval_tasks_tenant_id_fallback_role_code",
                schema: "workflow",
                table: "approval_tasks",
                columns: new[] { "tenant_id", "fallback_role_code" });

            migrationBuilder.CreateIndex(
                name: "ix_email_decision_tokens_tenant_id",
                schema: "workflow",
                table: "email_decision_tokens",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_email_decision_tokens_tenant_id_approval_task_id",
                schema: "workflow",
                table: "email_decision_tokens",
                columns: new[] { "tenant_id", "approval_task_id" });

            migrationBuilder.CreateIndex(
                name: "ix_email_decision_tokens_token_hash",
                schema: "workflow",
                table: "email_decision_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at_next_attempt_at",
                schema: "workflow",
                table: "outbox_messages",
                columns: new[] { "processed_at", "next_attempt_at" },
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_tenant_id",
                schema: "workflow",
                table: "outbox_messages",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "approval_history_entries",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "approval_policy_steps",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "approval_tasks",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "email_decision_tokens",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "approval_policies",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "approval_requests",
                schema: "workflow");
        }
    }
}

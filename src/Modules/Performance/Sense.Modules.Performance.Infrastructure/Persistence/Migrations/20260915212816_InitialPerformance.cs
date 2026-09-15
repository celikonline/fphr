using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Performance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPerformance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "performance");

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "performance",
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
                schema: "performance",
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
                name: "review_cycles",
                schema: "performance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    rating_scale = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    default_comment_mode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    evaluator_label_mode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    report_show_photo = table.Column<bool>(type: "boolean", nullable: false),
                    employee_can_view_results = table.Column<bool>(type: "boolean", nullable: false),
                    employee_can_download_pdf = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    published_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_by = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_review_cycles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "review_competencies",
                schema: "performance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    review_cycle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    weight = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_review_competencies", x => x.id);
                    table.ForeignKey(
                        name: "fk_review_competencies_review_cycles_review_cycle_id",
                        column: x => x.review_cycle_id,
                        principalSchema: "performance",
                        principalTable: "review_cycles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "review_participants",
                schema: "performance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    review_cycle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    include_goals = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    competency_progress = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    competency_score = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: true),
                    final_score = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: true),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_review_participants", x => x.id);
                    table.ForeignKey(
                        name: "fk_review_participants_review_cycles_review_cycle_id",
                        column: x => x.review_cycle_id,
                        principalSchema: "performance",
                        principalTable: "review_cycles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "review_relation_configs",
                schema: "performance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    review_cycle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    relation = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    weight = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    min_required = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_review_relation_configs", x => x.id);
                    table.ForeignKey(
                        name: "fk_review_relation_configs_review_cycles_review_cycle_id",
                        column: x => x.review_cycle_id,
                        principalSchema: "performance",
                        principalTable: "review_cycles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "review_questions",
                schema: "performance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    review_competency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    weight = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    comment_mode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_review_questions", x => x.id);
                    table.ForeignKey(
                        name: "fk_review_questions_review_competencies_review_competency_id",
                        column: x => x.review_competency_id,
                        principalSchema: "performance",
                        principalTable: "review_competencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "review_assignments",
                schema: "performance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    review_participant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    relation = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    evaluator_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_review_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_review_assignments_review_participants_review_participant_id",
                        column: x => x.review_participant_id,
                        principalSchema: "performance",
                        principalTable: "review_participants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "review_responses",
                schema: "performance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    review_assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_review_responses", x => x.id);
                    table.ForeignKey(
                        name: "fk_review_responses_review_assignments_review_assignment_id",
                        column: x => x.review_assignment_id,
                        principalSchema: "performance",
                        principalTable: "review_assignments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at_next_attempt_at",
                schema: "performance",
                table: "outbox_messages",
                columns: new[] { "processed_at", "next_attempt_at" },
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_tenant_id",
                schema: "performance",
                table: "outbox_messages",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_review_assignments_review_participant_id",
                schema: "performance",
                table: "review_assignments",
                column: "review_participant_id");

            migrationBuilder.CreateIndex(
                name: "ix_review_assignments_tenant_id",
                schema: "performance",
                table: "review_assignments",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_review_assignments_tenant_id_evaluator_employee_id",
                schema: "performance",
                table: "review_assignments",
                columns: new[] { "tenant_id", "evaluator_employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_review_competencies_review_cycle_id",
                schema: "performance",
                table: "review_competencies",
                column: "review_cycle_id");

            migrationBuilder.CreateIndex(
                name: "ix_review_competencies_tenant_id",
                schema: "performance",
                table: "review_competencies",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_review_cycles_tenant_id",
                schema: "performance",
                table: "review_cycles",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_review_cycles_tenant_id_status",
                schema: "performance",
                table: "review_cycles",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_review_participants_review_cycle_id_employee_id",
                schema: "performance",
                table: "review_participants",
                columns: new[] { "review_cycle_id", "employee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_review_participants_tenant_id",
                schema: "performance",
                table: "review_participants",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_review_participants_tenant_id_employee_id",
                schema: "performance",
                table: "review_participants",
                columns: new[] { "tenant_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_review_questions_review_competency_id",
                schema: "performance",
                table: "review_questions",
                column: "review_competency_id");

            migrationBuilder.CreateIndex(
                name: "ix_review_questions_tenant_id",
                schema: "performance",
                table: "review_questions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_review_relation_configs_review_cycle_id_relation",
                schema: "performance",
                table: "review_relation_configs",
                columns: new[] { "review_cycle_id", "relation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_review_relation_configs_tenant_id",
                schema: "performance",
                table: "review_relation_configs",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_review_responses_review_assignment_id_question_id",
                schema: "performance",
                table: "review_responses",
                columns: new[] { "review_assignment_id", "question_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_review_responses_tenant_id",
                schema: "performance",
                table: "review_responses",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "performance");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "performance");

            migrationBuilder.DropTable(
                name: "review_questions",
                schema: "performance");

            migrationBuilder.DropTable(
                name: "review_relation_configs",
                schema: "performance");

            migrationBuilder.DropTable(
                name: "review_responses",
                schema: "performance");

            migrationBuilder.DropTable(
                name: "review_competencies",
                schema: "performance");

            migrationBuilder.DropTable(
                name: "review_assignments",
                schema: "performance");

            migrationBuilder.DropTable(
                name: "review_participants",
                schema: "performance");

            migrationBuilder.DropTable(
                name: "review_cycles",
                schema: "performance");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Recruitment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormsAndAutomationRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "automation_rules",
                schema: "recruitment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_stage = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    to_stage = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    required_form_template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_automation_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "form_responses",
                schema: "recruitment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submitted_by_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    weighted_score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_form_responses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "form_templates",
                schema: "recruitment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    template_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_form_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "field_answers",
                schema: "recruitment",
                columns: table => new
                {
                    field_id = table.Column<Guid>(type: "uuid", nullable: false),
                    response_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_field_answers", x => new { x.response_id, x.field_id });
                    table.ForeignKey(
                        name: "fk_field_answers_form_responses_response_id",
                        column: x => x.response_id,
                        principalSchema: "recruitment",
                        principalTable: "form_responses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "form_fields",
                schema: "recruitment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    field_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    is_sensitive = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    options = table.Column<string>(type: "jsonb", nullable: false),
                    weight = table.Column<int>(type: "integer", nullable: true),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_form_fields", x => x.id);
                    table.ForeignKey(
                        name: "fk_form_fields_form_templates_template_id",
                        column: x => x.template_id,
                        principalSchema: "recruitment",
                        principalTable: "form_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_automation_rules_tenant_id",
                schema: "recruitment",
                table: "automation_rules",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_automation_rules_tenant_id_from_stage_to_stage_is_active",
                schema: "recruitment",
                table: "automation_rules",
                columns: new[] { "tenant_id", "from_stage", "to_stage", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_form_fields_template_id",
                schema: "recruitment",
                table: "form_fields",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ix_form_responses_job_application_id_template_id",
                schema: "recruitment",
                table: "form_responses",
                columns: new[] { "job_application_id", "template_id" });

            migrationBuilder.CreateIndex(
                name: "ix_form_responses_tenant_id",
                schema: "recruitment",
                table: "form_responses",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_form_responses_tenant_id_job_application_id",
                schema: "recruitment",
                table: "form_responses",
                columns: new[] { "tenant_id", "job_application_id" });

            migrationBuilder.CreateIndex(
                name: "ix_form_templates_tenant_id",
                schema: "recruitment",
                table: "form_templates",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_form_templates_tenant_id_is_active",
                schema: "recruitment",
                table: "form_templates",
                columns: new[] { "tenant_id", "is_active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "automation_rules",
                schema: "recruitment");

            migrationBuilder.DropTable(
                name: "field_answers",
                schema: "recruitment");

            migrationBuilder.DropTable(
                name: "form_fields",
                schema: "recruitment");

            migrationBuilder.DropTable(
                name: "form_responses",
                schema: "recruitment");

            migrationBuilder.DropTable(
                name: "form_templates",
                schema: "recruitment");
        }
    }
}

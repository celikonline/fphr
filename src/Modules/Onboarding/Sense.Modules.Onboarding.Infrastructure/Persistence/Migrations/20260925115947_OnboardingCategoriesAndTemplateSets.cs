using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Onboarding.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OnboardingCategoriesAndTemplateSets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "category_id",
                schema: "onboarding",
                table: "task_templates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "importance",
                schema: "onboarding",
                table: "task_templates",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Normal");

            migrationBuilder.AddColumn<bool>(
                name: "requires_e_document",
                schema: "onboarding",
                table: "task_templates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "category_id",
                schema: "onboarding",
                table: "employee_tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "importance",
                schema: "onboarding",
                table: "employee_tasks",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Normal");

            migrationBuilder.AddColumn<bool>(
                name: "requires_e_document",
                schema: "onboarding",
                table: "employee_tasks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "template_set_id",
                schema: "onboarding",
                table: "employee_tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "categories",
                schema: "onboarding",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "template_sets",
                schema: "onboarding",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    stage = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    template_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_template_sets", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_employee_tasks_tenant_id_employee_id_template_set_id",
                schema: "onboarding",
                table: "employee_tasks",
                columns: new[] { "tenant_id", "employee_id", "template_set_id" });

            migrationBuilder.CreateIndex(
                name: "ix_categories_tenant_id",
                schema: "onboarding",
                table: "categories",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_categories_tenant_id_sort_order",
                schema: "onboarding",
                table: "categories",
                columns: new[] { "tenant_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_template_sets_tenant_id",
                schema: "onboarding",
                table: "template_sets",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_template_sets_tenant_id_stage",
                schema: "onboarding",
                table: "template_sets",
                columns: new[] { "tenant_id", "stage" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "categories",
                schema: "onboarding");

            migrationBuilder.DropTable(
                name: "template_sets",
                schema: "onboarding");

            migrationBuilder.DropIndex(
                name: "ix_employee_tasks_tenant_id_employee_id_template_set_id",
                schema: "onboarding",
                table: "employee_tasks");

            migrationBuilder.DropColumn(
                name: "category_id",
                schema: "onboarding",
                table: "task_templates");

            migrationBuilder.DropColumn(
                name: "importance",
                schema: "onboarding",
                table: "task_templates");

            migrationBuilder.DropColumn(
                name: "requires_e_document",
                schema: "onboarding",
                table: "task_templates");

            migrationBuilder.DropColumn(
                name: "category_id",
                schema: "onboarding",
                table: "employee_tasks");

            migrationBuilder.DropColumn(
                name: "importance",
                schema: "onboarding",
                table: "employee_tasks");

            migrationBuilder.DropColumn(
                name: "requires_e_document",
                schema: "onboarding",
                table: "employee_tasks");

            migrationBuilder.DropColumn(
                name: "template_set_id",
                schema: "onboarding",
                table: "employee_tasks");
        }
    }
}

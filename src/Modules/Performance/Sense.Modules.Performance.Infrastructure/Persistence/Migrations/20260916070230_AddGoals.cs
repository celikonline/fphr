using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Performance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGoals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "goal_progress",
                schema: "performance",
                table: "review_participants",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "goal_score",
                schema: "performance",
                table: "review_participants",
                type: "numeric(7,2)",
                precision: 7,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "competency_weight",
                schema: "performance",
                table: "review_cycles",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "goal_entry_by",
                schema: "performance",
                table: "review_cycles",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "goal_categories",
                schema: "performance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goal_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "goals",
                schema: "performance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    review_participant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    target_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    actual_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    scale = table.Column<string>(type: "jsonb", nullable: false),
                    points = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: true),
                    result_comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    weight = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    entered_by = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    result_entered_by = table.Column<Guid>(type: "uuid", nullable: true),
                    result_approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goals", x => x.id);
                    table.ForeignKey(
                        name: "fk_goals_review_participants_review_participant_id",
                        column: x => x.review_participant_id,
                        principalSchema: "performance",
                        principalTable: "review_participants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_goal_categories_tenant_id",
                schema: "performance",
                table: "goal_categories",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_goal_categories_tenant_id_name",
                schema: "performance",
                table: "goal_categories",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_goals_category_id",
                schema: "performance",
                table: "goals",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_goals_review_participant_id",
                schema: "performance",
                table: "goals",
                column: "review_participant_id");

            migrationBuilder.CreateIndex(
                name: "ix_goals_tenant_id",
                schema: "performance",
                table: "goals",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "goal_categories",
                schema: "performance");

            migrationBuilder.DropTable(
                name: "goals",
                schema: "performance");

            migrationBuilder.DropColumn(
                name: "goal_progress",
                schema: "performance",
                table: "review_participants");

            migrationBuilder.DropColumn(
                name: "goal_score",
                schema: "performance",
                table: "review_participants");

            migrationBuilder.DropColumn(
                name: "competency_weight",
                schema: "performance",
                table: "review_cycles");

            migrationBuilder.DropColumn(
                name: "goal_entry_by",
                schema: "performance",
                table: "review_cycles");
        }
    }
}

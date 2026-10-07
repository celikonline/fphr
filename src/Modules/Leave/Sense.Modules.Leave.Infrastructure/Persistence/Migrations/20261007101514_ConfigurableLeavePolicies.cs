using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Leave.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConfigurableLeavePolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "feb29policy",
                schema: "leave",
                table: "leave_types",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "UseFebruary28");

            migrationBuilder.AddColumn<decimal>(
                name: "hourly_half_day_days",
                schema: "leave",
                table: "leave_types",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0.5m);

            migrationBuilder.AddColumn<int>(
                name: "hourly_half_day_threshold_minutes",
                schema: "leave",
                table: "leave_types",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "use_only_on_birthday",
                schema: "leave",
                table: "leave_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                UPDATE leave.leave_types AS leave_type
                SET use_only_on_birthday = TRUE
                FROM leave.rule_sets AS leave_rule_set
                WHERE leave_rule_set.id = leave_type.rule_set_id
                  AND leave_rule_set.is_system_seed = TRUE
                  AND leave_type.code = 'Birthday';

                UPDATE leave.leave_types AS leave_type
                SET hourly_half_day_threshold_minutes = 180,
                    hourly_half_day_days = 0.5
                FROM leave.rule_sets AS leave_rule_set
                WHERE leave_rule_set.id = leave_type.rule_set_id
                  AND leave_rule_set.is_system_seed = TRUE
                  AND leave_type.code IN ('JobSearch', 'Breastfeeding');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "feb29policy",
                schema: "leave",
                table: "leave_types");

            migrationBuilder.DropColumn(
                name: "hourly_half_day_days",
                schema: "leave",
                table: "leave_types");

            migrationBuilder.DropColumn(
                name: "hourly_half_day_threshold_minutes",
                schema: "leave",
                table: "leave_types");

            migrationBuilder.DropColumn(
                name: "use_only_on_birthday",
                schema: "leave",
                table: "leave_types");
        }
    }
}

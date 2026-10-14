using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Onboarding.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OnboardingBuddyAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "buddy_assignments",
                schema: "onboarding",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    buddy_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_buddy_assignments", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_buddy_assignments_tenant_id",
                schema: "onboarding",
                table: "buddy_assignments",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_buddy_assignments_tenant_id_buddy_employee_id",
                schema: "onboarding",
                table: "buddy_assignments",
                columns: new[] { "tenant_id", "buddy_employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_buddy_assignments_tenant_id_employee_id",
                schema: "onboarding",
                table: "buddy_assignments",
                columns: new[] { "tenant_id", "employee_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "buddy_assignments",
                schema: "onboarding");
        }
    }
}

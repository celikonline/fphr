using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.OccupationalSafety.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOhsServices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "committee_members",
                schema: "ohs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    person_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_committee_members", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "safety_assignments",
                schema: "ohs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    person_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    certificate_class = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    certificate_number = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    provider_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    monthly_minutes = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_safety_assignments", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_committee_members_tenant_id",
                schema: "ohs",
                table: "committee_members",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_committee_members_tenant_id_location_id",
                schema: "ohs",
                table: "committee_members",
                columns: new[] { "tenant_id", "location_id" });

            migrationBuilder.CreateIndex(
                name: "ix_safety_assignments_tenant_id",
                schema: "ohs",
                table: "safety_assignments",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_safety_assignments_tenant_id_location_id",
                schema: "ohs",
                table: "safety_assignments",
                columns: new[] { "tenant_id", "location_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "committee_members",
                schema: "ohs");

            migrationBuilder.DropTable(
                name: "safety_assignments",
                schema: "ohs");
        }
    }
}

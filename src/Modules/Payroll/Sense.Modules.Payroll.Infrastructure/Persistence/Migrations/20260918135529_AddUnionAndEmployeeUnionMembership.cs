using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Payroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUnionAndEmployeeUnionMembership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "employee_union_memberships",
                schema: "payroll",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    union_id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_date = table.Column<DateOnly>(type: "date", nullable: false),
                    notified_to_employer_at = table.Column<DateOnly>(type: "date", nullable: false),
                    coverage_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    dues_rule = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    dues_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_union_memberships", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "unions",
                schema: "payroll",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    industry_branch_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_employee_union_memberships_tenant_id",
                schema: "payroll",
                table: "employee_union_memberships",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_union_memberships_tenant_id_employee_id_membership",
                schema: "payroll",
                table: "employee_union_memberships",
                columns: new[] { "tenant_id", "employee_id", "membership_date" });

            migrationBuilder.CreateIndex(
                name: "ix_unions_tenant_id",
                schema: "payroll",
                table: "unions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_unions_tenant_id_name",
                schema: "payroll",
                table: "unions",
                columns: new[] { "tenant_id", "name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_union_memberships",
                schema: "payroll");

            migrationBuilder.DropTable(
                name: "unions",
                schema: "payroll");
        }
    }
}

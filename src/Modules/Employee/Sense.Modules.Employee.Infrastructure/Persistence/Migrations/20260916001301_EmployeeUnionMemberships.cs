using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Employee.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EmployeeUnionMemberships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "union_memberships",
                schema: "employee",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    protected_union_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    business_line_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    membership_date = table.Column<DateOnly>(type: "date", nullable: false),
                    employer_notified_date = table.Column<DateOnly>(type: "date", nullable: true),
                    coverage_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    is_union_officer = table.Column<bool>(type: "boolean", nullable: false),
                    officer_since = table.Column<DateOnly>(type: "date", nullable: true),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_union_memberships", x => x.id);
                    table.ForeignKey(
                        name: "fk_union_memberships_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "employee",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_union_memberships_employee_id",
                schema: "employee",
                table: "union_memberships",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_union_memberships_tenant_id",
                schema: "employee",
                table: "union_memberships",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "union_memberships",
                schema: "employee");
        }
    }
}

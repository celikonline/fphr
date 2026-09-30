using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Workflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovalDelegations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "decided_on_behalf_of_employee_id",
                schema: "workflow",
                table: "approval_tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "approval_delegations",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    delegator_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delegate_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    request_types = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_by_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approval_delegations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_approval_delegations_tenant_id",
                schema: "workflow",
                table: "approval_delegations",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_approval_delegations_tenant_id_delegate_employee_id",
                schema: "workflow",
                table: "approval_delegations",
                columns: new[] { "tenant_id", "delegate_employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_approval_delegations_tenant_id_delegator_employee_id",
                schema: "workflow",
                table: "approval_delegations",
                columns: new[] { "tenant_id", "delegator_employee_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "approval_delegations",
                schema: "workflow");

            migrationBuilder.DropColumn(
                name: "decided_on_behalf_of_employee_id",
                schema: "workflow",
                table: "approval_tasks");
        }
    }
}

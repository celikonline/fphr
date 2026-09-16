using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Recruitment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJobRequisitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "requisition_id",
                schema: "recruitment",
                table: "job_postings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "job_requisitions",
                schema: "recruitment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    org_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employment_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    headcount_requested = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    justification = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    requester_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_requisitions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_job_postings_requisition_id",
                schema: "recruitment",
                table: "job_postings",
                column: "requisition_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_requisitions_approval_request_id",
                schema: "recruitment",
                table: "job_requisitions",
                column: "approval_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_requisitions_tenant_id",
                schema: "recruitment",
                table: "job_requisitions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_requisitions_tenant_id_status",
                schema: "recruitment",
                table: "job_requisitions",
                columns: new[] { "tenant_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_requisitions",
                schema: "recruitment");

            migrationBuilder.DropIndex(
                name: "ix_job_postings_requisition_id",
                schema: "recruitment",
                table: "job_postings");

            migrationBuilder.DropColumn(
                name: "requisition_id",
                schema: "recruitment",
                table: "job_postings");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Assets.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssetAssignmentApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "approval_request_id",
                schema: "assets",
                table: "assets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "pending_employee_id",
                schema: "assets",
                table: "assets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "pending_issued_at",
                schema: "assets",
                table: "assets",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_assets_tenant_id_approval_request_id",
                schema: "assets",
                table: "assets",
                columns: new[] { "tenant_id", "approval_request_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_assets_tenant_id_approval_request_id",
                schema: "assets",
                table: "assets");

            migrationBuilder.DropColumn(
                name: "approval_request_id",
                schema: "assets",
                table: "assets");

            migrationBuilder.DropColumn(
                name: "pending_employee_id",
                schema: "assets",
                table: "assets");

            migrationBuilder.DropColumn(
                name: "pending_issued_at",
                schema: "assets",
                table: "assets");
        }
    }
}

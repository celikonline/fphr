using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvitationEmailSourceAndGrantApprovedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "batch_id",
                schema: "identity",
                table: "invitations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "email_source",
                schema: "identity",
                table: "invitations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Account");

            migrationBuilder.AddColumn<DateTime>(
                name: "approved_at",
                schema: "identity",
                table: "impersonation_grants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_invitations_batch_id",
                schema: "identity",
                table: "invitations",
                column: "batch_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_invitations_batch_id",
                schema: "identity",
                table: "invitations");

            migrationBuilder.DropColumn(
                name: "batch_id",
                schema: "identity",
                table: "invitations");

            migrationBuilder.DropColumn(
                name: "email_source",
                schema: "identity",
                table: "invitations");

            migrationBuilder.DropColumn(
                name: "approved_at",
                schema: "identity",
                table: "impersonation_grants");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Documents.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "expiry_date",
                schema: "documents",
                table: "documents",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_documents_tenant_id_expiry_date",
                schema: "documents",
                table: "documents",
                columns: new[] { "tenant_id", "expiry_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_documents_tenant_id_expiry_date",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "expiry_date",
                schema: "documents",
                table: "documents");
        }
    }
}

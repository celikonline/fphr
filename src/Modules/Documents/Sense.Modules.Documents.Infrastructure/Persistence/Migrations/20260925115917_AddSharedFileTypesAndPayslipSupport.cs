using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Documents.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedFileTypesAndPayslipSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "type_id",
                schema: "documents",
                table: "shared_company_files",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "visible_on_mobile",
                schema: "documents",
                table: "shared_company_files",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "shared_file_types",
                schema: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    visible_on_mobile = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shared_file_types", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shared_company_files_tenant_id_type_id",
                schema: "documents",
                table: "shared_company_files",
                columns: new[] { "tenant_id", "type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_shared_file_types_tenant_id",
                schema: "documents",
                table: "shared_file_types",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_shared_file_types_tenant_id_name",
                schema: "documents",
                table: "shared_file_types",
                columns: new[] { "tenant_id", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shared_file_types",
                schema: "documents");

            migrationBuilder.DropIndex(
                name: "ix_shared_company_files_tenant_id_type_id",
                schema: "documents",
                table: "shared_company_files");

            migrationBuilder.DropColumn(
                name: "type_id",
                schema: "documents",
                table: "shared_company_files");

            migrationBuilder.DropColumn(
                name: "visible_on_mobile",
                schema: "documents",
                table: "shared_company_files");
        }
    }
}

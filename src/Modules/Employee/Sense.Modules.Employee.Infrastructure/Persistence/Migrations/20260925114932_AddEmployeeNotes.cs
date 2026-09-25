using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Employee.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "employee_notes",
                schema: "employee",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    importance = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    attachment_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    attachment_storage_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    attachment_content_type = table.Column<string>(type: "character varying(127)", maxLength: 127, nullable: true),
                    attachment_size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    attachment_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_notes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_employee_notes_tenant_id",
                schema: "employee",
                table: "employee_notes",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_notes_tenant_id_employee_id_created_at",
                schema: "employee",
                table: "employee_notes",
                columns: new[] { "tenant_id", "employee_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_notes",
                schema: "employee");
        }
    }
}

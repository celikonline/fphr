using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Recruitment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RequisitionFieldTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "requisition_field_templates",
                schema: "recruitment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_requisition_field_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "requisition_field_template_rules",
                schema: "recruitment",
                columns: table => new
                {
                    field = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_visible = table.Column<bool>(type: "boolean", nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    default_value = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_requisition_field_template_rules", x => new { x.template_id, x.field });
                    table.ForeignKey(
                        name: "fk_requisition_field_template_rules_requisition_field_template",
                        column: x => x.template_id,
                        principalSchema: "recruitment",
                        principalTable: "requisition_field_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_requisition_field_templates_tenant_id",
                schema: "recruitment",
                table: "requisition_field_templates",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_requisition_field_templates_tenant_id_org_unit_id",
                schema: "recruitment",
                table: "requisition_field_templates",
                columns: new[] { "tenant_id", "org_unit_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "requisition_field_template_rules",
                schema: "recruitment");

            migrationBuilder.DropTable(
                name: "requisition_field_templates",
                schema: "recruitment");
        }
    }
}

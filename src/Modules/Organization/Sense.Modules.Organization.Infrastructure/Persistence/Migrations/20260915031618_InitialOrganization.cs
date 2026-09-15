using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Organization.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialOrganization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "org");

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "org",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    handler = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_messages", x => new { x.message_id, x.handler });
                });

            migrationBuilder.CreateTable(
                name: "org_unit_types",
                schema: "org",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    allows_nesting = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_org_unit_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "org",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload = table.Column<string>(type: "jsonb", maxLength: 500, nullable: false),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_dead = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "org_units",
                schema: "org",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    manager_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_org_units", x => x.id);
                    table.ForeignKey(
                        name: "fk_org_units_org_unit_types_type_id",
                        column: x => x.type_id,
                        principalSchema: "org",
                        principalTable: "org_unit_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_org_units_org_units_parent_id",
                        column: x => x.parent_id,
                        principalSchema: "org",
                        principalTable: "org_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "companies",
                schema: "org",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    holiday_calendar_id = table.Column<Guid>(type: "uuid", nullable: true),
                    joint_employment_group_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_companies", x => x.id);
                    table.ForeignKey(
                        name: "fk_companies_org_units_org_unit_id",
                        column: x => x.org_unit_id,
                        principalSchema: "org",
                        principalTable: "org_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "org_unit_approval_policy_assignments",
                schema: "org",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    approval_policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_org_unit_approval_policy_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_org_unit_approval_policy_assignments_org_units_org_unit_id",
                        column: x => x.org_unit_id,
                        principalSchema: "org",
                        principalTable: "org_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_companies_org_unit_id",
                schema: "org",
                table: "companies",
                column: "org_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_companies_tenant_id",
                schema: "org",
                table: "companies",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_companies_tenant_id_org_unit_id",
                schema: "org",
                table: "companies",
                columns: new[] { "tenant_id", "org_unit_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_org_unit_approval_policy_assignments_org_unit_id",
                schema: "org",
                table: "org_unit_approval_policy_assignments",
                column: "org_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_org_unit_approval_policy_assignments_tenant_id",
                schema: "org",
                table: "org_unit_approval_policy_assignments",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_org_unit_approval_policy_assignments_tenant_id_org_unit_id_",
                schema: "org",
                table: "org_unit_approval_policy_assignments",
                columns: new[] { "tenant_id", "org_unit_id", "request_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_org_unit_types_tenant_id",
                schema: "org",
                table: "org_unit_types",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_org_unit_types_tenant_id_code",
                schema: "org",
                table: "org_unit_types",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_org_unit_types_tenant_id_sort_order",
                schema: "org",
                table: "org_unit_types",
                columns: new[] { "tenant_id", "sort_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_org_units_location_id",
                schema: "org",
                table: "org_units",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_org_units_manager_employee_id",
                schema: "org",
                table: "org_units",
                column: "manager_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_org_units_parent_id",
                schema: "org",
                table: "org_units",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_org_units_tenant_id",
                schema: "org",
                table: "org_units",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_org_units_tenant_id_parent_id_name",
                schema: "org",
                table: "org_units",
                columns: new[] { "tenant_id", "parent_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_org_units_tenant_id_type_id",
                schema: "org",
                table: "org_units",
                columns: new[] { "tenant_id", "type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_org_units_type_id",
                schema: "org",
                table: "org_units",
                column: "type_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at_next_attempt_at",
                schema: "org",
                table: "outbox_messages",
                columns: new[] { "processed_at", "next_attempt_at" },
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_tenant_id",
                schema: "org",
                table: "outbox_messages",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "companies",
                schema: "org");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "org");

            migrationBuilder.DropTable(
                name: "org_unit_approval_policy_assignments",
                schema: "org");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "org");

            migrationBuilder.DropTable(
                name: "org_units",
                schema: "org");

            migrationBuilder.DropTable(
                name: "org_unit_types",
                schema: "org");
        }
    }
}

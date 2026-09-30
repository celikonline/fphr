using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Notifications.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HrMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hr_messages",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    body = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    importance = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    channels = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "message_groups",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    selection_json = table.Column<string>(type: "jsonb", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_message_groups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "hr_message_recipients",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_message_recipients", x => x.id);
                    table.ForeignKey(
                        name: "fk_hr_message_recipients_hr_messages_message_id",
                        column: x => x.message_id,
                        principalSchema: "notifications",
                        principalTable: "hr_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_hr_message_recipients_message_id",
                schema: "notifications",
                table: "hr_message_recipients",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_message_recipients_tenant_id",
                schema: "notifications",
                table: "hr_message_recipients",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_message_recipients_tenant_id_user_id",
                schema: "notifications",
                table: "hr_message_recipients",
                columns: new[] { "tenant_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_hr_messages_tenant_id",
                schema: "notifications",
                table: "hr_messages",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_messages_tenant_id_sender_user_id_created_at",
                schema: "notifications",
                table: "hr_messages",
                columns: new[] { "tenant_id", "sender_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_message_groups_tenant_id",
                schema: "notifications",
                table: "message_groups",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_message_groups_tenant_id_name",
                schema: "notifications",
                table: "message_groups",
                columns: new[] { "tenant_id", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hr_message_recipients",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "message_groups",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "hr_messages",
                schema: "notifications");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Notifications.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "notifications");

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "notifications",
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
                name: "notification_deliveries",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    recipient_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recipient_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recipient_address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    recipient_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    address_source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    culture = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    model_json = table.Column<string>(type: "jsonb", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_deliveries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notification_preferences",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_preferences", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "notifications",
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
                name: "user_notifications",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    action_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    source_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    read_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_notifications", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notification_deliveries_status_next_attempt_at",
                schema: "notifications",
                table: "notification_deliveries",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_deliveries_tenant_id",
                schema: "notifications",
                table: "notification_deliveries",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_notification_deliveries_tenant_id_source_event_id_template_",
                schema: "notifications",
                table: "notification_deliveries",
                columns: new[] { "tenant_id", "source_event_id", "template_key", "channel", "recipient_address" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_preferences_tenant_id",
                schema: "notifications",
                table: "notification_preferences",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_notification_preferences_tenant_id_user_id_template_key_cha",
                schema: "notifications",
                table: "notification_preferences",
                columns: new[] { "tenant_id", "user_id", "template_key", "channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at_next_attempt_at",
                schema: "notifications",
                table: "outbox_messages",
                columns: new[] { "processed_at", "next_attempt_at" },
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_tenant_id",
                schema: "notifications",
                table: "outbox_messages",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_notifications_tenant_id",
                schema: "notifications",
                table: "user_notifications",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_notifications_tenant_id_source_event_id_template_key_u",
                schema: "notifications",
                table: "user_notifications",
                columns: new[] { "tenant_id", "source_event_id", "template_key", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_notifications_tenant_id_user_id_created_at",
                schema: "notifications",
                table: "user_notifications",
                columns: new[] { "tenant_id", "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_user_notifications_tenant_id_user_id_read_at",
                schema: "notifications",
                table: "user_notifications",
                columns: new[] { "tenant_id", "user_id", "read_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "notification_deliveries",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "notification_preferences",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "user_notifications",
                schema: "notifications");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Announcements.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRecognitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "recognitions",
                schema: "announcements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receiver_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recognitions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_recognitions_tenant_id",
                schema: "announcements",
                table: "recognitions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_recognitions_tenant_id_created_at",
                schema: "announcements",
                table: "recognitions",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_recognitions_tenant_id_receiver_employee_id",
                schema: "announcements",
                table: "recognitions",
                columns: new[] { "tenant_id", "receiver_employee_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recognitions",
                schema: "announcements");
        }
    }
}

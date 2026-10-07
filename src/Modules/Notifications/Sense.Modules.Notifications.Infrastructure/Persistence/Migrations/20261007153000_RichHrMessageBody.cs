using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Notifications.Infrastructure.Persistence.Migrations;

public partial class RichHrMessageBody : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AlterColumn<string>(
            name: "body",
            schema: "notifications",
            table: "hr_messages",
            type: "character varying(1000000)",
            maxLength: 1000000,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(4096)",
            oldMaxLength: 4096);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AlterColumn<string>(
            name: "body",
            schema: "notifications",
            table: "hr_messages",
            type: "character varying(4096)",
            maxLength: 4096,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(1000000)",
            oldMaxLength: 1000000);
}

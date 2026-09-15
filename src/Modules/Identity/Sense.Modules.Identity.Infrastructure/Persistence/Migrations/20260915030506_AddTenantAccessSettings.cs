using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantAccessSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "access_settings",
                schema: "identity",
                table: "tenants",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "access_settings",
                schema: "identity",
                table: "tenants");
        }
    }
}

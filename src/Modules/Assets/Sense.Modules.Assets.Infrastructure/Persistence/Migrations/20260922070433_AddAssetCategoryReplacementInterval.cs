using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Assets.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetCategoryReplacementInterval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "replacement_interval_days",
                schema: "assets",
                table: "asset_categories",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "replacement_interval_days",
                schema: "assets",
                table: "asset_categories");
        }
    }
}

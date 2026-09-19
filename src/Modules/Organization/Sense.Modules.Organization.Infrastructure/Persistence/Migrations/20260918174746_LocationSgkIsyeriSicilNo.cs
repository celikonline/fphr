using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Organization.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LocationSgkIsyeriSicilNo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "sgk_isyeri_sicil_no",
                schema: "org",
                table: "locations",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "sgk_isyeri_sicil_no",
                schema: "org",
                table: "locations");
        }
    }
}

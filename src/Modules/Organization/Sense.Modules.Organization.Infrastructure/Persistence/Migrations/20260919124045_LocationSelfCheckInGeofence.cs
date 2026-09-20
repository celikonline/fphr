using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Organization.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LocationSelfCheckInGeofence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "geofence_radius_meters",
                schema: "org",
                table: "locations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "latitude",
                schema: "org",
                table: "locations",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "longitude",
                schema: "org",
                table: "locations",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "self_check_in_code",
                schema: "org",
                table: "locations",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "");

            // Backfill: the column was added with a '' default, which would collide on the (tenant_id, self_check_in_code)
            // unique index for any tenant that already has 2+ locations. gen_random_uuid() is volatile (evaluated per row) and
            // CSPRNG-backed (PostgreSQL 13+); 12 upper-case hex chars = 48 random bits. New locations get a 12-char 31-symbol
            // code from the domain, and admins can rotate any code via POST .../self-checkin-code/regenerate.
            migrationBuilder.Sql(
                "UPDATE org.locations SET self_check_in_code = upper(substr(replace(gen_random_uuid()::text, '-', ''), 1, 12)) WHERE self_check_in_code = '';");

            migrationBuilder.CreateIndex(
                name: "ix_locations_tenant_id_self_check_in_code",
                schema: "org",
                table: "locations",
                columns: new[] { "tenant_id", "self_check_in_code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_locations_tenant_id_self_check_in_code",
                schema: "org",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "geofence_radius_meters",
                schema: "org",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "latitude",
                schema: "org",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "longitude",
                schema: "org",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "self_check_in_code",
                schema: "org",
                table: "locations");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Organization.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyProfileFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "address",
                schema: "org",
                table: "companies",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "city",
                schema: "org",
                table: "companies",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "country",
                schema: "org",
                table: "companies",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "logo_document_id",
                schema: "org",
                table: "companies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "logo_url",
                schema: "org",
                table: "companies",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "operating_hours_end",
                schema: "org",
                table: "companies",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "operating_hours_start",
                schema: "org",
                table: "companies",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "registration_number",
                schema: "org",
                table: "companies",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tax_number",
                schema: "org",
                table: "companies",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "working_days",
                schema: "org",
                table: "companies",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "address", schema: "org", table: "companies");
            migrationBuilder.DropColumn(name: "city", schema: "org", table: "companies");
            migrationBuilder.DropColumn(name: "country", schema: "org", table: "companies");
            migrationBuilder.DropColumn(name: "logo_document_id", schema: "org", table: "companies");
            migrationBuilder.DropColumn(name: "logo_url", schema: "org", table: "companies");
            migrationBuilder.DropColumn(name: "operating_hours_end", schema: "org", table: "companies");
            migrationBuilder.DropColumn(name: "operating_hours_start", schema: "org", table: "companies");
            migrationBuilder.DropColumn(name: "registration_number", schema: "org", table: "companies");
            migrationBuilder.DropColumn(name: "tax_number", schema: "org", table: "companies");
            migrationBuilder.DropColumn(name: "working_days", schema: "org", table: "companies");
        }
    }
}

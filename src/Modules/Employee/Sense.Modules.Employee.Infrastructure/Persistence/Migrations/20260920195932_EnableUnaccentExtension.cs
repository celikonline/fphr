using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Employee.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnableUnaccentExtension : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Employee search folds accents (ş/ı/ğ/İ → s/i/g/I) with unaccent(); docker-compose's init.sql creates
            // it, this makes fresh, managed and test databases work as well.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS unaccent;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}

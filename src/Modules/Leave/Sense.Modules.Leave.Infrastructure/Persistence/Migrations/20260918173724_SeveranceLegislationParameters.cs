using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Sense.Modules.Leave.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeveranceLegislationParameters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "leave",
                table: "legislation_parameters",
                columns: new[] { "id", "country", "created_at", "created_user_id", "decimal_value", "effective_from", "effective_to", "key", "modified_date", "modified_user_id", "source", "value_json" },
                values: new object[,]
                {
                    { new Guid("00000008-0000-0000-0000-000000000040"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 55000.00m, new DateOnly(2026, 1, 1), null, "Severance.CeilingGross", null, null, "Taslak 2026 bordro parametresi - mali müşavirle doğrulanmalı (11.3.07-bordro.md) - kıdem tazminatı tavanı (bir hizmet yılı için azami brüt tutar, İş K. m.14)", null },
                    { new Guid("00000008-0000-0000-0000-000000000041"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, null, new DateOnly(2026, 1, 1), null, "Notice.WeeksBySeniorityTiers", null, null, "4857 s. İş Kanunu md.17 - ihbar önel tablosu (6 aya kadar 2 hafta, 6 ay-1,5 yıl 4 hafta, 1,5-3 yıl 6 hafta, 3 yıldan fazla 8 hafta)", "[{\"upperBoundDays\":183,\"weeks\":2},{\"upperBoundDays\":548,\"weeks\":4},{\"upperBoundDays\":1095,\"weeks\":6},{\"upperBoundDays\":null,\"weeks\":8}]" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000040"));

            migrationBuilder.DeleteData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000041"));
        }
    }
}

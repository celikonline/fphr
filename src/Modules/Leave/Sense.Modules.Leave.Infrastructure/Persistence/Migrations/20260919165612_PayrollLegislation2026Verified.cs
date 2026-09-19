using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Leave.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PayrollLegislation2026Verified : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000025"),
                columns: new[] { "decimal_value", "source" },
                values: new object[] { 33030.00m, "2026 asgari ücret, Resmî Gazete (yürürlük 01.01.2026); kaynak: https://www.csgb.gov.tr/Media/gm2fekds/asgari-%C3%BCcret-2026.pdf - aylık brüt asgari ücret" });

            migrationBuilder.UpdateData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000026"),
                columns: new[] { "decimal_value", "source" },
                values: new object[] { 33030.00m, "5510 s. Kanun md.82; 2026 asgari ücret, Resmî Gazete (yürürlük 01.01.2026); kaynak: https://www.csgb.gov.tr/Media/gm2fekds/asgari-%C3%BCcret-2026.pdf - SGK prim matrahı alt sınırı (asgari ücrete eşit)" });

            migrationBuilder.UpdateData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000027"),
                columns: new[] { "decimal_value", "source" },
                values: new object[] { 247725.00m, "5510 s. Kanun md.82; 2026 asgari ücret, Resmî Gazete (yürürlük 01.01.2026); kaynak: https://www.csgb.gov.tr/Media/gm2fekds/asgari-%C3%BCcret-2026.pdf - SGK prim matrahı üst sınırı (asgari ücretin 7,5 katı = 247.725 TL)" });

            migrationBuilder.UpdateData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000036"),
                columns: new[] { "source", "value_json" },
                values: new object[] { "193 s. GVK md.103 - 2026 ücret gelirleri tarifesi (%15/20/27/35/40; 190.000/400.000/1.500.000/5.300.000), 332 Sıra No.lu Gelir Vergisi Genel Tebliği; kaynak: https://www.memurlar.net/haber/1155986/2026-gelir-vergisi-dilimlerini-gosteren-teblig-r-g-de-yayimlandi.html", "[{\"upperBound\":190000,\"rate\":0.15},{\"upperBound\":400000,\"rate\":0.20},{\"upperBound\":1500000,\"rate\":0.27},{\"upperBound\":5300000,\"rate\":0.35},{\"upperBound\":null,\"rate\":0.40}]" });

            migrationBuilder.UpdateData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000040"),
                columns: new[] { "decimal_value", "effective_to", "source" },
                values: new object[] { 64948.77m, new DateOnly(2026, 6, 30), "Hazine ve Maliye Bakanlığı Mali ve Sosyal Haklar Genelgesi (kıdem tazminatı tavanı, 6 ayda bir); kaynak: https://www.csgb.gov.tr/%C4%B1statistikler/calisma-hayati-%C4%B1statistikleri/kidem-tazminati-tavan-miktari/ - 01.01.2026-30.06.2026 kıdem tazminatı tavanı 64.948,77 TL (bir hizmet yılı için azami brüt tutar, İş K. m.14)" });

            migrationBuilder.InsertData(
                schema: "leave",
                table: "legislation_parameters",
                columns: new[] { "id", "country", "created_at", "created_user_id", "decimal_value", "effective_from", "effective_to", "key", "modified_date", "modified_user_id", "source", "value_json" },
                values: new object[] { new Guid("00000008-0000-0000-0000-000000000046"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 73729.87m, new DateOnly(2026, 7, 1), null, "Severance.CeilingGross", null, null, "Hazine ve Maliye Bakanlığı Mali ve Sosyal Haklar Genelgesi (kıdem tazminatı tavanı, 6 ayda bir); kaynak: https://www.csgb.gov.tr/%C4%B1statistikler/calisma-hayati-%C4%B1statistikleri/kidem-tazminati-tavan-miktari/ - 01.07.2026-31.12.2026 kıdem tazminatı tavanı 73.729,87 TL", null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000046"));

            migrationBuilder.UpdateData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000025"),
                columns: new[] { "decimal_value", "source" },
                values: new object[] { 30000.00m, "Taslak 2026 bordro parametresi - mali müşavirle doğrulanmalı (11.3.07-bordro.md) - aylık brüt asgari ücret" });

            migrationBuilder.UpdateData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000026"),
                columns: new[] { "decimal_value", "source" },
                values: new object[] { 30000.00m, "Taslak 2026 bordro parametresi - mali müşavirle doğrulanmalı (11.3.07-bordro.md) - SGK prim matrahı alt sınırı (asgari ücrete eşit)" });

            migrationBuilder.UpdateData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000027"),
                columns: new[] { "decimal_value", "source" },
                values: new object[] { 225000.00m, "Taslak 2026 bordro parametresi - mali müşavirle doğrulanmalı (11.3.07-bordro.md) - SGK prim matrahı üst sınırı (asgari ücretin 7,5 katı)" });

            migrationBuilder.UpdateData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000036"),
                columns: new[] { "source", "value_json" },
                values: new object[] { "Taslak 2026 bordro parametresi - mali müşavirle doğrulanmalı (11.3.07-bordro.md) - GVK md.103 kümülatif gelir vergisi tarifesi (%15/20/27/35/40)", "[{\"upperBound\":158000,\"rate\":0.15},{\"upperBound\":330000,\"rate\":0.20},{\"upperBound\":1200000,\"rate\":0.27},{\"upperBound\":4300000,\"rate\":0.35},{\"upperBound\":null,\"rate\":0.40}]" });

            migrationBuilder.UpdateData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000040"),
                columns: new[] { "decimal_value", "effective_to", "source" },
                values: new object[] { 55000.00m, null, "Taslak 2026 bordro parametresi - mali müşavirle doğrulanmalı (11.3.07-bordro.md) - kıdem tazminatı tavanı (bir hizmet yılı için azami brüt tutar, İş K. m.14)" });
        }
    }
}

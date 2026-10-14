using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Leave.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PayrollIncentiveParameters2026 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "leave", table: "legislation_parameters", keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000036"),
                columns: new[] { "source" },
                values: new object[] { "193 s. GVK md.103 - 2026 ücret gelirleri tarifesi (%15/20/27/35/40; 190.000/400.000/1.500.000/5.300.000), 332 Sıra No.lu Gelir Vergisi Genel Tebliği; GİB: https://gib.gov.tr/mevzuat/kanun/433/madde/6937" });

            migrationBuilder.UpdateData(
                schema: "leave", table: "legislation_parameters", keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000039"),
                columns: new[] { "source" },
                values: new object[] { "2026-10-14 resmi kaynak kontrolü - market harcamasına açık kartın yemek istisnası kapsamına girdiği kabul edilmez; kart türü/harcama kısıtı doğrulanmalı" });

            migrationBuilder.UpdateData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000034"),
                columns: new[] { "decimal_value", "source" },
                values: new object[] { 300.00m, "193 s. GVK md.23/8 ve 2026 gelir vergisi tarifesi; GİB: https://usak.gib.gov.tr/vergi-konulari/1_bireysel/11_ucret_geliri/11 - nakit/yemek kartı günlük GV istisnası" });

            migrationBuilder.UpdateData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000035"),
                columns: new[] { "decimal_value", "key", "source" },
                values: new object[] { 158.00m, "Payroll.Meal.Sgk.DailyExemption", "SGK 2026 prime esas kazanç miktarları: https://www.sgk.gov.tr/Content/Post/2e0c9e1a-2cfe-4456-af10-49d3de0c58ba/Prime-Esas-Kazanc-Miktarlari-2026-01-14-10-35-39 - fiilen çalışılan gün başına SGK yemek istisnası 158 TL" });

            migrationBuilder.UpdateData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000043"),
                columns: new[] { "decimal_value", "source" },
                values: new object[] { 12000.00m, "193 s. GVK md.31; 332 Seri No.lu Tebliğ - GİB 2026 engellilik indirimi: https://gib.gov.tr/vergi-konulari/1_bireysel/8_engelliler_icin_vergi_avantajlari/8" });

            migrationBuilder.UpdateData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000044"),
                columns: new[] { "decimal_value", "source" },
                values: new object[] { 7000.00m, "193 s. GVK md.31; 332 Seri No.lu Tebliğ - GİB 2026 engellilik indirimi: https://gib.gov.tr/vergi-konulari/1_bireysel/8_engelliler_icin_vergi_avantajlari/8" });

            migrationBuilder.UpdateData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000045"),
                columns: new[] { "decimal_value", "source" },
                values: new object[] { 3000.00m, "193 s. GVK md.31; 332 Seri No.lu Tebliğ - GİB 2026 engellilik indirimi: https://gib.gov.tr/vergi-konulari/1_bireysel/8_engelliler_icin_vergi_avantajlari/8" });

            migrationBuilder.UpdateData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000027"),
                columns: new[] { "decimal_value", "source" },
                values: new object[] { 297270.00m, "5510 md.82, 7566 s.K. - 2026 SGK tavanı asgari ücretin 9 katı; 2026 asgari ücret, Resmî Gazete (yürürlük 01.01.2026); kaynak: https://www.csgb.gov.tr/Media/gm2fekds/asgari-%C3%BCcret-2026.pdf" });

            migrationBuilder.UpdateData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000030"),
                columns: new[] { "decimal_value", "source" },
                values: new object[] { 0.2175m, "5510 md.81, 7566 s.K. (01.01.2026); SGK işveren prim oranları: https://www.sgk.gov.tr/Content/Post/c7812ea8-5087-413f-aeb5-d3c1d153e11a/Isveren-Prim-Oranlari-2026-01-13-04-52-38 - indirimsiz işveren SGK oranı %21,75 (işsizlik hariç)" });

            migrationBuilder.UpdateData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000032"),
                columns: new[] { "decimal_value", "source" },
                values: new object[] { 0.02m, "SGK 2026/1 Genelgesi; https://sgk.gov.tr/Content/Post/c52f99f0-da6c-4029-9b9a-a0d6f96a6a42/Guncel-Sigorta-Primi-Tesvik-Destek-ve-Indirimleri-2026-08-04-10-29-55 - genel sektör 2 puan indirimi; imalat 5 puan modeli ayrı atanır" });

            migrationBuilder.InsertData(
                schema: "leave",
                table: "legislation_parameters",
                columns: new[] { "id", "country", "created_at", "created_user_id", "decimal_value", "effective_from", "effective_to", "key", "modified_date", "modified_user_id", "source", "value_json" },
                values: new object[] { new Guid("00000008-0000-0000-0000-000000000047"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 0.205m, new DateOnly(2026, 1, 1), null, "Payroll.Sgk.UnemploymentBeneficiaryEmployerSupportRate", null, null, "4447 md.50/5 - uzun vadeli ve GSS işveren payları (%19,5) + kısa vadeli %1; 5510 md.81, 7566 s.K. (01.01.2026); SGK işveren prim oranları: https://www.sgk.gov.tr/Content/Post/c7812ea8-5087-413f-aeb5-d3c1d153e11a/Isveren-Prim-Oranlari-2026-01-13-04-52-38", null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "leave", table: "legislation_parameters", keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000036"),
                columns: new[] { "source" },
                values: new object[] { "193 s. GVK md.103 - 2026 ücret gelirleri tarifesi (%15/20/27/35/40; 190.000/400.000/1.500.000/5.300.000), 332 Sıra No.lu Gelir Vergisi Genel Tebliği; kaynak: https://www.memurlar.net/haber/1155986/2026-gelir-vergisi-dilimlerini-gosteren-teblig-r-g-de-yayimlandi.html" });

            migrationBuilder.UpdateData(
                schema: "leave", table: "legislation_parameters", keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000039"),
                columns: new[] { "source" },
                values: new object[] { "Taslak 2026 bordro parametresi - mali müşavirle doğrulanmalı (11.3.07-bordro.md) - yemek+market kartı varyantına Cash/MealCardOnly istisnası uygulanmıyor (0 = hayır), tamamı matraha girer" });

            migrationBuilder.DeleteData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000047"));

            migrationBuilder.UpdateData(
                schema: "leave", table: "legislation_parameters", keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000034"),
                columns: new[] { "decimal_value", "source" },
                values: new object[] { 240.00m, "Taslak 2026 bordro parametresi - mali müşavirle doğrulanmalı (11.3.07-bordro.md) - nakit/yalnız yemek kartı günlük gelir vergisi istisnası" });

            migrationBuilder.UpdateData(
                schema: "leave", table: "legislation_parameters", keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000035"),
                columns: new[] { "decimal_value", "key", "source" },
                values: new object[] { 0.2365m, "Payroll.Meal.Sgk.DailyExemptionRate", "Taslak 2026 bordro parametresi - mali müşavirle doğrulanmalı (11.3.07-bordro.md) - SGK Genelgesi yaklaşımı: günlük brüt asgari ücretin %23,65'i SGK'dan istisna" });

            migrationBuilder.UpdateData(
                schema: "leave", table: "legislation_parameters", keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000043"),
                columns: new[] { "decimal_value", "source" },
                values: new object[] { 6900.00m, "193 s. GVK md.31 - I. derece (en ağır) engellilik indirimi, Taslak 2026 bordro parametresi - mali müşavirle doğrulanmalı (11.3.07-bordro.md)" });

            migrationBuilder.UpdateData(
                schema: "leave", table: "legislation_parameters", keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000044"),
                columns: new[] { "decimal_value", "source" },
                values: new object[] { 4000.00m, "193 s. GVK md.31 - II. derece engellilik indirimi, Taslak 2026 bordro parametresi - mali müşavirle doğrulanmalı (11.3.07-bordro.md)" });

            migrationBuilder.UpdateData(
                schema: "leave", table: "legislation_parameters", keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000045"),
                columns: new[] { "decimal_value", "source" },
                values: new object[] { 1700.00m, "193 s. GVK md.31 - III. derece engellilik indirimi, Taslak 2026 bordro parametresi - mali müşavirle doğrulanmalı (11.3.07-bordro.md)" });

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
                keyValue: new Guid("00000008-0000-0000-0000-000000000030"),
                columns: new[] { "decimal_value", "source" },
                values: new object[] { 0.205m, "5510 s. Kanun md.81 - SGK işveren hissesi (indirimsiz) %20,5" });

            migrationBuilder.UpdateData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000032"),
                columns: new[] { "decimal_value", "source" },
                values: new object[] { 0.05m, "5510 s. Kanun md.81 - 5 puanlık SGK işveren teşviki oranı (uygunluk hesabı henüz uygulanmıyor, TODO P2+)" });
        }
    }
}

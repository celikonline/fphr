using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Sense.Modules.Leave.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PayrollLegislationParameters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "decimal_value",
                schema: "leave",
                table: "legislation_parameters",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,4)",
                oldPrecision: 18,
                oldScale: 4,
                oldNullable: true);

            migrationBuilder.InsertData(
                schema: "leave",
                table: "legislation_parameters",
                columns: new[] { "id", "country", "created_at", "created_user_id", "decimal_value", "effective_from", "effective_to", "key", "modified_date", "modified_user_id", "source", "value_json" },
                values: new object[,]
                {
                    { new Guid("00000008-0000-0000-0000-000000000025"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 30000.00m, new DateOnly(2026, 1, 1), null, "Payroll.MinimumWage.Gross", null, null, "Taslak 2026 bordro parametresi - mali müşavirle doğrulanmalı (11.3.07-bordro.md) - aylık brüt asgari ücret", null },
                    { new Guid("00000008-0000-0000-0000-000000000026"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 30000.00m, new DateOnly(2026, 1, 1), null, "Payroll.Sgk.Floor", null, null, "Taslak 2026 bordro parametresi - mali müşavirle doğrulanmalı (11.3.07-bordro.md) - SGK prim matrahı alt sınırı (asgari ücrete eşit)", null },
                    { new Guid("00000008-0000-0000-0000-000000000027"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 225000.00m, new DateOnly(2026, 1, 1), null, "Payroll.Sgk.Ceiling", null, null, "Taslak 2026 bordro parametresi - mali müşavirle doğrulanmalı (11.3.07-bordro.md) - SGK prim matrahı üst sınırı (asgari ücretin 7,5 katı)", null },
                    { new Guid("00000008-0000-0000-0000-000000000028"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 0.14m, new DateOnly(2026, 1, 1), null, "Payroll.Sgk.EmployeeRate", null, null, "5510 s. Kanun md.81 - SGK işçi hissesi (malullük/yaşlılık/ölüm + genel sağlık) %14", null },
                    { new Guid("00000008-0000-0000-0000-000000000029"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 0.01m, new DateOnly(2026, 1, 1), null, "Payroll.Unemployment.EmployeeRate", null, null, "4447 s. İşsizlik Sigortası Kanunu md.49 - işsizlik sigortası işçi payı %1", null },
                    { new Guid("00000008-0000-0000-0000-000000000030"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 0.205m, new DateOnly(2026, 1, 1), null, "Payroll.Sgk.EmployerRate", null, null, "5510 s. Kanun md.81 - SGK işveren hissesi (indirimsiz) %20,5", null },
                    { new Guid("00000008-0000-0000-0000-000000000031"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 0.02m, new DateOnly(2026, 1, 1), null, "Payroll.Unemployment.EmployerRate", null, null, "4447 s. İşsizlik Sigortası Kanunu md.49 - işsizlik sigortası işveren payı %2", null },
                    { new Guid("00000008-0000-0000-0000-000000000032"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 0.05m, new DateOnly(2026, 1, 1), null, "Payroll.Sgk.EmployerDiscountRate", null, null, "5510 s. Kanun md.81 - 5 puanlık SGK işveren teşviki oranı (uygunluk hesabı henüz uygulanmıyor, TODO P2+)", null },
                    { new Guid("00000008-0000-0000-0000-000000000033"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 0.00759m, new DateOnly(2026, 1, 1), null, "Payroll.StampTax.Rate", null, null, "488 s. Damga Vergisi Kanunu (1) sayılı tablo IV/1-b - ücretlerde binde 7,59", null },
                    { new Guid("00000008-0000-0000-0000-000000000034"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 240.00m, new DateOnly(2026, 1, 1), null, "Payroll.Meal.IncomeTax.DailyExemption", null, null, "Taslak 2026 bordro parametresi - mali müşavirle doğrulanmalı (11.3.07-bordro.md) - nakit/yalnız yemek kartı günlük gelir vergisi istisnası", null },
                    { new Guid("00000008-0000-0000-0000-000000000035"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 0.2365m, new DateOnly(2026, 1, 1), null, "Payroll.Meal.Sgk.DailyExemptionRate", null, null, "Taslak 2026 bordro parametresi - mali müşavirle doğrulanmalı (11.3.07-bordro.md) - SGK Genelgesi yaklaşımı: günlük brüt asgari ücretin %23,65'i SGK'dan istisna", null },
                    { new Guid("00000008-0000-0000-0000-000000000036"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, null, new DateOnly(2026, 1, 1), null, "Payroll.IncomeTax.Brackets", null, null, "Taslak 2026 bordro parametresi - mali müşavirle doğrulanmalı (11.3.07-bordro.md) - GVK md.103 kümülatif gelir vergisi tarifesi (%15/20/27/35/40)", "[{\"upperBound\":158000,\"rate\":0.15},{\"upperBound\":330000,\"rate\":0.20},{\"upperBound\":1200000,\"rate\":0.27},{\"upperBound\":4300000,\"rate\":0.35},{\"upperBound\":null,\"rate\":0.40}]" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000025"));

            migrationBuilder.DeleteData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000026"));

            migrationBuilder.DeleteData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000027"));

            migrationBuilder.DeleteData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000028"));

            migrationBuilder.DeleteData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000029"));

            migrationBuilder.DeleteData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000030"));

            migrationBuilder.DeleteData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000031"));

            migrationBuilder.DeleteData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000032"));

            migrationBuilder.DeleteData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000033"));

            migrationBuilder.DeleteData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000034"));

            migrationBuilder.DeleteData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000035"));

            migrationBuilder.DeleteData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000036"));

            migrationBuilder.AlterColumn<decimal>(
                name: "decimal_value",
                schema: "leave",
                table: "legislation_parameters",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)",
                oldPrecision: 18,
                oldScale: 6,
                oldNullable: true);
        }
    }
}

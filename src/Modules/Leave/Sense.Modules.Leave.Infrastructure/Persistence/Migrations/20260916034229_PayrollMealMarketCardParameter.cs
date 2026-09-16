using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Sense.Modules.Leave.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PayrollMealMarketCardParameter : Migration
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
                    { new Guid("00000008-0000-0000-0000-000000000037"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 30m, new DateOnly(2003, 6, 10), null, "JobSecurity.MinHeadcount", null, null, "4857 s. İş Kanunu md.18 (RG 10.06.2003/25134) - iş güvencesi kapsamı: 30 ve üzeri işçi çalıştıran işyeri (yaklaşık, iş kolu gruplaması hariç)", null },
                    { new Guid("00000008-0000-0000-0000-000000000038"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 6m, new DateOnly(2003, 6, 10), null, "JobSecurity.MinSeniorityMonths", null, null, "4857 s. İş Kanunu md.18 (RG 10.06.2003/25134) - iş güvencesi kapsamı: en az 6 aylık kıdem", null },
                    { new Guid("00000008-0000-0000-0000-000000000039"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 0m, new DateOnly(2026, 1, 1), null, "Payroll.Meal.MarketCard.ExemptionApplies", null, null, "Taslak 2026 bordro parametresi - mali müşavirle doğrulanmalı (11.3.07-bordro.md) - yemek+market kartı varyantına Cash/MealCardOnly istisnası uygulanmıyor (0 = hayır), tamamı matraha girer", null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000037"));

            migrationBuilder.DeleteData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000038"));

            migrationBuilder.DeleteData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000039"));
        }
    }
}

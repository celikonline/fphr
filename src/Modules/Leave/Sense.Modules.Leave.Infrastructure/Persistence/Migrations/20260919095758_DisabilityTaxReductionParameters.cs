using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Sense.Modules.Leave.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DisabilityTaxReductionParameters : Migration
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
                    { new Guid("00000008-0000-0000-0000-000000000043"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 6900.00m, new DateOnly(2026, 1, 1), null, "Payroll.Disability.MonthlyTaxReduction.Degree1", null, null, "193 s. GVK md.31 - I. derece (en ağır) engellilik indirimi, Taslak 2026 bordro parametresi - mali müşavirle doğrulanmalı (11.3.07-bordro.md)", null },
                    { new Guid("00000008-0000-0000-0000-000000000044"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 4000.00m, new DateOnly(2026, 1, 1), null, "Payroll.Disability.MonthlyTaxReduction.Degree2", null, null, "193 s. GVK md.31 - II. derece engellilik indirimi, Taslak 2026 bordro parametresi - mali müşavirle doğrulanmalı (11.3.07-bordro.md)", null },
                    { new Guid("00000008-0000-0000-0000-000000000045"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 1700.00m, new DateOnly(2026, 1, 1), null, "Payroll.Disability.MonthlyTaxReduction.Degree3", null, null, "193 s. GVK md.31 - III. derece engellilik indirimi, Taslak 2026 bordro parametresi - mali müşavirle doğrulanmalı (11.3.07-bordro.md)", null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000043"));

            migrationBuilder.DeleteData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000044"));

            migrationBuilder.DeleteData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000045"));
        }
    }
}

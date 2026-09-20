using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Leave.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SenseIk036LeaveRuleSeedCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "leave",
                table: "legislation_parameters",
                columns: new[] { "id", "country", "created_at", "created_user_id", "decimal_value", "effective_from", "effective_to", "key", "modified_date", "modified_user_id", "source", "value_json" },
                values: new object[] { new Guid("00000008-0000-0000-0000-000000000042"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 1.5m, new DateOnly(2003, 6, 10), null, "Leave.Breastfeeding.HoursPerDay", null, null, "4857 s. İş Kanunu md.74 (RG 10.06.2003/25134)/son - 1 yaşından küçük çocuk için günde toplam süt izni (saat)", null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "leave",
                table: "legislation_parameters",
                keyColumn: "id",
                keyValue: new Guid("00000008-0000-0000-0000-000000000042"));
        }
    }
}

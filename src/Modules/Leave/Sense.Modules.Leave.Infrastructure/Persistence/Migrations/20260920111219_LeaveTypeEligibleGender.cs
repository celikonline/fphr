using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Leave.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LeaveTypeEligibleGender : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "eligible_gender",
                schema: "leave",
                table: "leave_types",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Any");

            // Denetim raporu A6: mevcut kiracıların yasal türleri de cinsiyet uygunluğunu alır (yalnız bir kez; sonraki HR düzenlemeleri korunur).
            migrationBuilder.Sql("UPDATE leave.leave_types SET eligible_gender = 'Male' WHERE code = 'Paternity';");
            migrationBuilder.Sql("UPDATE leave.leave_types SET eligible_gender = 'Female' WHERE code IN ('Maternity', 'Breastfeeding');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "eligible_gender",
                schema: "leave",
                table: "leave_types");
        }
    }
}

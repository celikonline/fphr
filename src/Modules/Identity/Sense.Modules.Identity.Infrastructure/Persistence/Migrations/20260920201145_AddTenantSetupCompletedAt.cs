using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantSetupCompletedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "setup_completed_at",
                schema: "identity",
                table: "tenants",
                type: "timestamp with time zone",
                nullable: true);

            // Kurulum sihirbazı bu migrasyondan ÖNCE var olan kiracılar için geçerli değildir: mevcut şirketler zaten kullanımda,
            // onları zorunlu sihirbaza sokmak yerine oluşturulma anlarıyla "tamamlanmış" işaretlenir. Yeni kiracılar (süper admin
            // ile açılanlar) null başlar ve Owner'a sihirbaz gösterilir.
            migrationBuilder.Sql(
                "UPDATE identity.tenants SET setup_completed_at = created_at WHERE setup_completed_at IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "setup_completed_at",
                schema: "identity",
                table: "tenants");
        }
    }
}

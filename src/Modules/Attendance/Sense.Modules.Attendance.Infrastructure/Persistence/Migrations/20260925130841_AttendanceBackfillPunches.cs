using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Attendance.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Zaman-devam B1 (Z0.1) veri taşıması: şema değişikliği yok. Okutması olmayan her <c>attendance_days</c> satırının giriş/çıkışından
    /// ham okutma (<c>punches</c>) üretir - C# eşi <c>Application.Punches.LegacyDayPunches</c> ile aynı kural: giriş = In, çıkış = Out (çıkış
    /// girişten önce/eşitse ertesi gün, gece vardiyası), ölçülmüş PDKS molası (<c>actual_break_minutes</c>) varsa giriş-çıkış aralığının
    /// ortasına o uzunlukta temsili bir Out→In çifti; kaynak = günün kaynağı, Manual ise <c>is_manual</c>. Yerel saat kiracının saat dilimiyle
    /// (<c>identity.tenants.time_zone</c>; yoksa/geçersizse UTC - TenantLocalClock ile aynı geri düşüş) UTC'ye çevrilir. Self-servis günlerin
    /// okutmaları aynı anın <c>self_check_ins</c> satırına bağlanır (<c>source_ref</c> = SelfCheckIn id, konum/koordinat kopyalanır).
    /// İdempotent: okutması olan günlere dokunmaz, tekillik çakışmasını yoksayar. Geri alma yalnız bu taşımanın gerekçesini taşıyan
    /// okutmaları siler. Sonuç: okutmalardan yeniden türetilen gün taşımadan önceki değerlerle aynıdır (BackfillMigrationTests).
    /// </summary>
    public partial class AttendanceBackfillPunches : Migration
    {
        /// <summary><c>Application.Punches.PunchDayWriter.LegacyReason</c> ile aynı değer.</summary>
        public const string BackfillReason = "Mevcut puantaj gününden taşındı";

        /// <summary>Taşıma betiği (testte tekrar koşturulup idempotans doğrulanır).</summary>
        public const string BackfillSql = """
            CREATE TEMP TABLE IF NOT EXISTS b1_tenant_zones (tenant_id uuid PRIMARY KEY, zone text NOT NULL);
            TRUNCATE b1_tenant_zones;

            DO $b1$
            BEGIN
                IF to_regclass('identity.tenants') IS NOT NULL THEN
                    INSERT INTO b1_tenant_zones (tenant_id, zone)
                    SELECT t.id, t.time_zone
                    FROM identity.tenants t
                    WHERE t.time_zone IN (SELECT name FROM pg_timezone_names);
                END IF;
            END
            $b1$;

            WITH days AS (
                SELECT d.tenant_id, d.employee_id, d.date AS work_date, d.source, d.actual_break_minutes, COALESCE(z.zone, 'UTC') AS zone,
                       d.date + d.check_in AS in_at,
                       CASE
                           WHEN d.check_out IS NULL THEN NULL
                           WHEN d.check_in IS NOT NULL AND d.check_out <= d.check_in THEN (d.date + 1) + d.check_out
                           ELSE d.date + d.check_out
                       END AS out_at
                FROM attendance.attendance_days d
                LEFT JOIN b1_tenant_zones z ON z.tenant_id = d.tenant_id
                WHERE (d.check_in IS NOT NULL OR d.check_out IS NOT NULL)
                  AND NOT EXISTS (
                      SELECT 1 FROM attendance.punches p
                      WHERE p.tenant_id = d.tenant_id AND p.employee_id = d.employee_id AND p.work_date = d.date AND p.deleted_at_utc IS NULL)
            ),
            breaks AS (
                SELECT days.*, make_interval(mins => days.actual_break_minutes) AS break_span
                FROM days
                WHERE days.in_at IS NOT NULL AND days.out_at IS NOT NULL AND days.actual_break_minutes > 0
                  AND days.out_at - days.in_at > make_interval(mins => days.actual_break_minutes)
            ),
            local_punches AS (
                SELECT tenant_id, employee_id, work_date, source, zone, 'In' AS direction, in_at AS at FROM days WHERE in_at IS NOT NULL
                UNION ALL
                SELECT tenant_id, employee_id, work_date, source, zone, 'Out', out_at FROM days WHERE out_at IS NOT NULL
                UNION ALL
                SELECT tenant_id, employee_id, work_date, source, zone, 'Out', in_at + (out_at - in_at - break_span) / 2 FROM breaks
                UNION ALL
                SELECT tenant_id, employee_id, work_date, source, zone, 'In', in_at + (out_at - in_at - break_span) / 2 + break_span FROM breaks
            )
            INSERT INTO attendance.punches (id, tenant_id, employee_id, occurred_at_utc, work_date, direction, source, is_manual, reason, created_at)
            SELECT gen_random_uuid(), lp.tenant_id, lp.employee_id, date_trunc('microseconds', lp.at AT TIME ZONE lp.zone), lp.work_date, lp.direction,
                   lp.source, lp.source = 'Manual', 'Mevcut puantaj gününden taşındı', now()
            FROM local_punches lp
            ON CONFLICT DO NOTHING;

            UPDATE attendance.punches p
            SET source_ref = s.id::text, location_id = s.location_id, latitude = s.latitude, longitude = s.longitude
            FROM attendance.self_check_ins s
            WHERE p.reason = 'Mevcut puantaj gününden taşındı' AND p.source = 'SelfService' AND p.source_ref IS NULL AND p.deleted_at_utc IS NULL
              AND s.tenant_id = p.tenant_id AND s.employee_id = p.employee_id AND s.direction = p.direction
              AND date_trunc('microseconds', s.timestamp_utc) = p.occurred_at_utc;

            DROP TABLE IF EXISTS b1_tenant_zones;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(BackfillSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DELETE FROM attendance.punches WHERE reason = '{BackfillReason}';");
        }
    }
}

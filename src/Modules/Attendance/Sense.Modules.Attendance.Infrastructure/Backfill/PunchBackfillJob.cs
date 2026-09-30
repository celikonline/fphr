using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sense.Modules.Attendance.Application.Punches;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Punches;
using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Sense.Modules.Attendance.Infrastructure.Persistence;
using Sense.Modules.Identity.Contracts;
using Sense.Shared.Infrastructure.Persistence;

namespace Sense.Modules.Attendance.Infrastructure.Backfill;

/// <summary>Geri doldurma sonucu: okutma üretilen gün ve okutma sayısı.</summary>
public sealed record PunchBackfillResult(int Days, int Punches);

/// <summary>
/// Zaman-devam B1 (Z0.1) tek seferlik, idempotent geri doldurma: okutma tabanlı hesaplamadan önce kaydedilmiş her <see cref="AttendanceDay"/>'in
/// giriş/çıkışından ham okutma üretir (kural: <see cref="LegacyDayPunches"/> - çalışma anındaki eski gün dönüşümüyle aynı). Şema değişikliği
/// yoktur; Sense.Migrator'da migrate sonrası adım olarak çalışır (<see cref="AttendanceDataBackfill"/>).
/// <list type="bullet">
/// <item>Kiracı saat dilimi Identity.Contracts <see cref="ITenantTimeZoneLookup"/> üzerinden (yoksa UTC - TenantLocalClock ile aynı).</item>
/// <item>Self-servis günlerin okutmalarına aynı anın <see cref="SelfCheckIn"/> satırındaki konum/koordinat kopyalanır.</item>
/// <item>Her okutma işaretlidir (<c>SourceRef</c> = <c>backfill:&lt;gün-id&gt;</c>, mola çifti <c>backfill-break:&lt;gün-id&gt;</c>).</item>
/// <item>İdempotent: işareti (silinmiş okutmalarda da) ya da canlı okutması olan güne dokunmaz; tekrar koşu hiçbir şey üretmez.</item>
/// <item>Aynı anda iki koşu kiracı başına transaction düzeyli advisory kilitle sıraya girer.</item>
/// </list>
/// Geri alma: <c>DELETE FROM attendance.punches WHERE source_ref LIKE 'backfill:%' OR source_ref LIKE 'backfill-break:%'</c> (bkz. handoff B1).
/// </summary>
public sealed partial class PunchBackfillJob(AttendanceDbContext db, ITenantTimeZoneLookup timeZones, ILogger<PunchBackfillJob> logger)
{
    public const int BatchSize = 500;

    /// <summary>pg_advisory_xact_lock anahtarı (sabit, "B1PUNCH" türevi).</summary>
    private const long AdvisoryLockKey = 0x42_31_50_55_4E_43_48;

    public async Task<PunchBackfillResult> RunAsync(CancellationToken cancellationToken)
    {
        var tenantIds = await Days().Select(d => d.TenantId).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);
        var total = new PunchBackfillResult(0, 0);
        foreach (var tenantId in tenantIds)
        {
            var zone = await ZoneAsync(tenantId, cancellationToken).ConfigureAwait(false);
            var done = await RunTenantAsync(tenantId, zone, cancellationToken).ConfigureAwait(false);
            total = new PunchBackfillResult(total.Days + done.Days, total.Punches + done.Punches);
        }

        LogCompleted(logger, total.Days, total.Punches);
        return total;
    }

    private async Task<PunchBackfillResult> RunTenantAsync(Guid tenantId, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        var result = new PunchBackfillResult(0, 0);
        await db.ExecuteInTransactionAsync(
            async ct =>
            {
                result = new PunchBackfillResult(0, 0);
                await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({AdvisoryLockKey})", ct).ConfigureAwait(false);

                // Günler değişmez (yalnız okutma eklenir), sıra kimliğe göre sabit: sayfalama güvenli.
                for (var offset = 0; ; offset += BatchSize)
                {
                    var batch = await Days()
                        .Where(d => d.TenantId == tenantId)
                        .OrderBy(d => d.Id)
                        .Skip(offset)
                        .Take(BatchSize)
                        .AsNoTracking()
                        .ToListAsync(ct)
                        .ConfigureAwait(false);
                    if (batch.Count == 0)
                    {
                        break;
                    }

                    var done = await BackfillBatchAsync(tenantId, zone, batch, ct).ConfigureAwait(false);
                    result = new PunchBackfillResult(result.Days + done.Days, result.Punches + done.Punches);
                }
            },
            cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async Task<PunchBackfillResult> BackfillBatchAsync(Guid tenantId, TimeZoneInfo zone, List<AttendanceDay> batch, CancellationToken cancellationToken)
    {
        var employees = batch.Select(d => d.EmployeeId).Distinct().ToList();
        var from = batch.Min(d => d.Date);
        var to = batch.Max(d => d.Date);
        var markers = batch.Select(d => LegacyDayPunches.MarkerPrefix + d.Id).ToList();

        var marked = (await AllPunches(tenantId).Where(p => p.SourceRef != null && markers.Contains(p.SourceRef)).Select(p => p.SourceRef!)
            .ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
        var busy = (await AllPunches(tenantId)
                .Where(p => p.DeletedAtUtc == null && employees.Contains(p.EmployeeId) && p.WorkDate >= from && p.WorkDate <= to)
                .Select(p => new { p.EmployeeId, p.WorkDate })
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(p => (p.EmployeeId, p.WorkDate)).ToHashSet();
        var audits = await SelfCheckInsAsync(tenantId, batch, employees, from, to, cancellationToken).ConfigureAwait(false);

        var days = 0;
        var created = 0;
        foreach (var day in batch.Where(d => !marked.Contains(LegacyDayPunches.MarkerPrefix + d.Id) && !busy.Contains((d.EmployeeId, d.Date))))
        {
            var punches = LegacyDayPunches.From(day, zone, (utc, direction) => audits.GetValueOrDefault((day.EmployeeId, utc, direction)));
            db.Punches.AddRange(punches);
            days += punches.Count > 0 ? 1 : 0;
            created += punches.Count;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.Clear();
        return new PunchBackfillResult(days, created);
    }

    private async Task<Dictionary<(Guid EmployeeId, DateTime At, PunchDirection Direction), SelfCheckIn>> SelfCheckInsAsync(
        Guid tenantId, List<AttendanceDay> batch, List<Guid> employees, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (!batch.Any(d => d.Source == AttendanceDaySource.SelfService))
        {
            return [];
        }

        // Yerel gün ile UTC anı arasında en çok bir gün + gece vardiyası çıkışı için bir gün pay.
        var fromUtc = from.AddDays(-1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = to.AddDays(2).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var rows = await db.SelfCheckIns.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && employees.Contains(s.EmployeeId) && s.TimestampUtc >= fromUtc && s.TimestampUtc < toUtc)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows
            .GroupBy(s => (s.EmployeeId, Punch.NormalizeOccurredAt(s.TimestampUtc), s.Direction))
            .ToDictionary(g => g.Key, g => g.First());
    }

    private async Task<TimeZoneInfo> ZoneAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var zoneId = await timeZones.FindTimeZoneIdAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return zoneId is not null && TimeZoneInfo.TryFindSystemTimeZoneById(zoneId, out var found) ? found : TimeZoneInfo.Utc;
    }

    private IQueryable<AttendanceDay> Days() =>
        db.AttendanceDays.IgnoreQueryFilters([ModuleDbContext.TenantFilter]).Where(d => d.CheckIn != null || d.CheckOut != null);

    private IQueryable<Punch> AllPunches(Guid tenantId) =>
        db.Punches.IgnoreQueryFilters([ModuleDbContext.TenantFilter]).Where(p => p.TenantId == tenantId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Okutma geri doldurma tamamlandı: {Days} gün, {Punches} okutma")]
    private static partial void LogCompleted(ILogger logger, int days, int punches);
}

/// <summary>Attendance'ın migrate sonrası veri adımları (Sense.Migrator "migrate" komutundan sonra çağrılır).</summary>
public static class AttendanceDataBackfill
{
    /// <summary>Migrator ve testler için tek giriş noktası: geri doldurma işini çalıştırır.</summary>
    public static Task<PunchBackfillResult> RunAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<PunchBackfillJob>().RunAsync(cancellationToken);

    /// <summary>İşi kaydeder; <see cref="ITenantTimeZoneLookup"/> çağıranın kaydıdır (API/Worker: Identity; Migrator: Identity altyapısı).</summary>
    public static IServiceCollection AddAttendancePunchBackfill(this IServiceCollection services)
    {
        services.AddScoped<PunchBackfillJob>();
        return services;
    }
}

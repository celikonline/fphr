using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Attendance.Application.Punches;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Punches;
using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Sense.Modules.Attendance.Infrastructure;
using Sense.Modules.Attendance.Infrastructure.Backfill;
using Sense.Modules.Attendance.Infrastructure.Persistence;
using Sense.Modules.Attendance.Tests.Characterization;
using Sense.Modules.Identity.Contracts;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.PunchPipeline;

/// <summary>
/// B1 geri doldurma işi (<see cref="PunchBackfillJob"/>, Sense.Migrator'da migrate sonrası adım) gerçek PostgreSQL'de: okutması olmayan
/// günler (karakterizasyon ayı + self-servis günü + yalnız girişli gün) → iş → okutmalardan yeniden türetilen gün değerleri önceki ile birebir
/// aynı; iş iki kez koşunca çift kayıt yok; üretilen okutmalar işaretli (<c>backfill:</c> / <c>backfill-break:</c>) ve kiracı saat dilimi
/// Identity.Contracts üzerinden. Şema değişikliği yok.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class PunchBackfillJobTests : AttendanceCharacterizationFixture
{
    private const string TenantZone = "Europe/Istanbul";

    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById(TenantZone);
    private static readonly DateOnly SelfServiceDay = CharacterizationMonth.Date(13);
    private static readonly DateOnly OpenDay = CharacterizationMonth.Date(14);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _locationId = Guid.NewGuid();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        var zones = Substitute.For<ITenantTimeZoneLookup>();
        zones.FindTimeZoneIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TenantZone);
        services.AddSingleton(zones);
        services.AddAttendancePunchBackfill();
    }

    [Fact]
    public async Task Backfilled_days_rebuild_to_exactly_the_values_before_the_backfill()
    {
        var ct = TestContext.Current.CancellationToken;
        var (selfIn, selfOut) = await SeedAsync(ct);
        var before = await SnapshotDaysAsync(ct);

        var result = await RunJobAsync(ct);

        var punches = await LoadPunchesAsync(ct);
        result.ShouldBe(new PunchBackfillResult(before.Count(d => d.CheckIn is not null || d.CheckOut is not null), punches.Count));
        foreach (var day in before)
        {
            var dayPunches = punches.Where(p => p.WorkDate == day.Date).ToList();
            Describe(Rebuild(day, dayPunches)).ShouldBe(Describe(day), $"{day.Date} yeniden türetilince değişti");
            dayPunches.Select(p => (p.OccurredAtUtc, p.Direction)).ShouldBe(
                LegacyDayPunches.From(day, Zone).Select(p => (p.OccurredAtUtc, p.Direction)), $"{day.Date} iş ve çalışma anı taşıması ayrıştı");
            dayPunches.ShouldAllBe(p => p.SourceRef == LegacyDayPunches.MarkerPrefix + day.Id || p.SourceRef == LegacyDayPunches.BreakMarkerPrefix + day.Id);
        }

        punches.ShouldNotContain(p => p.WorkDate == CharacterizationMonth.Date(7), "giriş/çıkışı olmayan güne okutma üretilmez");
        punches.ShouldAllBe(p => p.Reason == PunchDayWriter.LegacyReason && p.TenantId == _tenantId && p.EmployeeId == _employeeId);
        punches.Where(p => p.WorkDate == CharacterizationMonth.Date(3)).ShouldAllBe(p => p.Source == PunchSource.Manual && p.IsManual);
        punches.First(p => p.WorkDate == CharacterizationMonth.Date(3)).OccurredAtUtc.ShouldBe(new DateTime(2026, 8, 3, 6, 0, 0, DateTimeKind.Utc));
        punches.Where(p => p.WorkDate == CharacterizationMonth.Date(11)).Select(p => p.SourceRef!.Split(':')[0])
            .ShouldBe(["backfill", "backfill-break", "backfill-break", "backfill"], "PDKS molası işaretli temsili Out→In çiftiyle korunur");
        punches.Where(p => p.WorkDate == CharacterizationMonth.Date(10)).Last().OccurredAtUtc.ShouldBe(new DateTime(2026, 8, 11, 3, 0, 0, DateTimeKind.Utc));

        var selfService = punches.Where(p => p.WorkDate == SelfServiceDay).ToList();
        selfService.Select(p => p.OccurredAtUtc).ShouldBe([selfIn.TimestampUtc, selfOut.TimestampUtc]);
        selfService.ShouldAllBe(p => p.Source == PunchSource.SelfService && p.LocationId == _locationId && !p.IsManual);
    }

    [Fact]
    public async Task Running_the_backfill_twice_creates_no_duplicates_even_after_backfilled_punches_were_deleted()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(ct);
        await RunJobAsync(ct);
        var first = await LoadPunchesAsync(ct);
        first.Count.ShouldBeGreaterThan(0);

        var second = await RunJobAsync(ct);

        second.ShouldBe(new PunchBackfillResult(0, 0));
        (await LoadPunchesAsync(ct)).Count.ShouldBe(first.Count);

        // Kullanıcı taşınmış bir günün okutmalarını sildiyse iş onları yeniden üretmez (işaret silinmişlerde de aranır).
        using (var scope = ServiceProvider!.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
            var day3 = await db.Punches.IgnoreQueryFilters().Where(p => p.WorkDate == CharacterizationMonth.Date(3)).ToListAsync(ct);
            day3.ForEach(p => p.SoftDelete(Guid.NewGuid(), "yanlış gün", DateTime.UtcNow));
            await db.SaveChangesAsync(ct);
        }

        (await RunJobAsync(ct)).ShouldBe(new PunchBackfillResult(0, 0));
    }

    private async Task<PunchBackfillResult> RunJobAsync(CancellationToken ct)
    {
        using var scope = ServiceProvider!.CreateScope();
        return await AttendanceDataBackfill.RunAsync(scope.ServiceProvider, ct);
    }

    private async Task<(SelfCheckIn In, SelfCheckIn Out)> SeedAsync(CancellationToken ct)
    {
        using (TenantContextSetter!.BeginScope(_tenantId))
        {
            await SeedMixedMonthAsync(_tenantId, _employeeId, ct);
            using var scope = ServiceProvider!.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();

            // Self-servis gün: 09:00 / 18:30 İstanbul; araya düşen ikinci giriş dokunuşu günü değiştirmemişti (yalnız denetim satırı).
            var selfIn = SelfCheckIn.RecordQr(_tenantId, _employeeId, new DateTime(2026, 8, 13, 6, 0, 0, DateTimeKind.Utc), PunchDirection.In, _locationId);
            var doubleTap = SelfCheckIn.RecordQr(_tenantId, _employeeId, new DateTime(2026, 8, 13, 6, 5, 0, DateTimeKind.Utc), PunchDirection.In, _locationId);
            var selfOut = SelfCheckIn.RecordQr(_tenantId, _employeeId, new DateTime(2026, 8, 13, 15, 30, 0, DateTimeKind.Utc), PunchDirection.Out, _locationId);
            db.SelfCheckIns.AddRange(selfIn, doubleTap, selfOut);
            db.AttendanceDays.Add(AttendanceDay.Record(
                _tenantId, _employeeId, SelfServiceDay, new TimeOnly(9, 0), new TimeOnly(18, 30), new TimeOnly(9, 0), new TimeOnly(18, 0), 480, 60,
                AttendanceDaySource.SelfService));
            db.AttendanceDays.Add(AttendanceDay.Record(
                _tenantId, _employeeId, OpenDay, new TimeOnly(9, 15), null, new TimeOnly(9, 0), new TimeOnly(18, 0), 480, 60, AttendanceDaySource.Device));
            await db.SaveChangesAsync(ct);
            return (selfIn, selfOut);
        }
    }

    private async Task<List<AttendanceDay>> SnapshotDaysAsync(CancellationToken ct)
    {
        using var scope = ServiceProvider!.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AttendanceDbContext>().AttendanceDays.IgnoreQueryFilters().AsNoTracking()
            .Where(d => d.EmployeeId == _employeeId).OrderBy(d => d.Date).ToListAsync(ct);
    }

    private async Task<IReadOnlyList<Punch>> LoadPunchesAsync(CancellationToken ct)
    {
        using var scope = ServiceProvider!.CreateScope();
        var repository = new PunchRepository(scope.ServiceProvider.GetRequiredService<AttendanceDbContext>());
        return await repository.ListForRangeAsync(_tenantId, CharacterizationMonth.Date(1), CharacterizationMonth.Date(31), [_employeeId], ct);
    }

    /// <summary>Okutmalardan yeniden türetme: günün kayıt anındaki plan anlık görüntüsüyle (kanalların kullandığı aynı Record yolu).</summary>
    private AttendanceDay Rebuild(AttendanceDay before, IReadOnlyList<Punch> punches)
    {
        var plan = before.PlannedStart is not null ? new PunchPlanWindow(before.PlannedStart, before.PlannedEnd, before.BreakMinutes) : PunchPlanWindow.NoWindow;
        var result = DayFromPunches.Build(punches, plan, Zone);
        result.Incalculable.ShouldBeFalse($"{before.Date} taşıma sonrası hesaplanamaz olmamalı");
        return AttendanceDay.Record(
            _tenantId, _employeeId, before.Date, result.CheckIn, result.CheckOut, before.PlannedStart, before.PlannedEnd, before.PlannedMinutes,
            before.BreakMinutes, before.Source, result.Overnight, result.ActualBreakMinutes, ShiftLimits.None);
    }

    private static string Describe(AttendanceDay d) =>
        $"{d.Date:dd} In={d.CheckIn} Out={d.CheckOut} W={d.WorkedMinutes} N={d.NetWorkedMinutes} AB={d.ActualBreakMinutes} B={d.BreakMinutesApplied} " +
        $"Ni={d.NightMinutes} OT={d.OvertimeMinutes} L={d.DailyLimitExceeded}/{d.NightLimitExceeded} A={d.Anomalies} S={d.Source}";
}

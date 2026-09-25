using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Attendance.Application.Punches;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Punches;
using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Sense.Modules.Attendance.Infrastructure.Persistence;
using Sense.Modules.Attendance.Infrastructure.Persistence.Migrations;
using Sense.Modules.Attendance.Tests.Characterization;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.PunchPipeline;

/// <summary>
/// B1 veri taşıması (<see cref="AttendanceBackfillPunches"/>) gerçek PostgreSQL'de: W0-a şemasında kaydedilmiş günler (karakterizasyon
/// ayı + self-servis günü + yalnız girişli gün) → taşıma → okutmalardan yeniden türetilen gün değerleri taşıma öncesiyle birebir aynı;
/// taşıma idempotent, geri alma yalnız taşınan okutmaları siler, C# eşi (<see cref="LegacyDayPunches"/>) aynı anları üretir.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class BackfillMigrationTests : AttendanceCharacterizationFixture
{
    private const string SchemaMigration = "20260925113345_AddPunchShiftDefinitionRotationTerminal";
    private const string TenantZone = "Europe/Istanbul";

    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById(TenantZone);
    private static readonly DateOnly SelfServiceDay = CharacterizationMonth.Date(13);
    private static readonly DateOnly OpenDay = CharacterizationMonth.Date(14);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _locationId = Guid.NewGuid();

    /// <summary>Veritabanı taşımadan hemen önceki şemada bırakılır; kiracı saat dilimi Identity tablosunun asgari bir eşinden okunur.</summary>
    protected override async Task OnDatabaseReadyAsync()
    {
        var db = ServiceProvider!.GetRequiredService<AttendanceDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "CREATE SCHEMA IF NOT EXISTS identity; CREATE TABLE IF NOT EXISTS identity.tenants (id uuid PRIMARY KEY, time_zone varchar(60) NOT NULL);");
        await db.GetService<IMigrator>().MigrateAsync(SchemaMigration);
    }

    [Fact]
    public async Task Backfilled_days_rebuild_to_exactly_the_values_before_the_migration()
    {
        var ct = TestContext.Current.CancellationToken;
        using var tenantScope = TenantContextSetter!.BeginScope(_tenantId);
        var (selfIn, selfOut) = await SeedAsync(ct);
        var before = await SnapshotDaysAsync(ct);

        await MigrateToLatestAsync(ct);

        var punches = await LoadPunchesAsync(ct);
        foreach (var day in before)
        {
            var dayPunches = punches.Where(p => p.WorkDate == day.Date).ToList();
            var rebuilt = Rebuild(day, dayPunches);
            Describe(rebuilt).ShouldBe(Describe(day), $"{day.Date} yeniden türetilince değişti");
            dayPunches.Select(p => (p.OccurredAtUtc, p.Direction)).ShouldBe(
                LegacyDayPunches.From(day, Zone).Select(p => (p.OccurredAtUtc, p.Direction)), $"{day.Date} SQL ve C# taşıması ayrıştı");
        }

        punches.ShouldNotContain(p => p.WorkDate == CharacterizationMonth.Date(7), "giriş/çıkışı olmayan güne okutma üretilmez");
        punches.ShouldAllBe(p => p.Reason == PunchDayWriter.LegacyReason && p.TenantId == _tenantId && p.EmployeeId == _employeeId);
        punches.Where(p => p.WorkDate == CharacterizationMonth.Date(3)).ShouldAllBe(p => p.Source == PunchSource.Manual && p.IsManual);
        punches.First(p => p.WorkDate == CharacterizationMonth.Date(3)).OccurredAtUtc.ShouldBe(new DateTime(2026, 8, 3, 6, 0, 0, DateTimeKind.Utc));
        punches.Where(p => p.WorkDate == CharacterizationMonth.Date(11)).Select(p => p.Direction)
            .ShouldBe([PunchDirection.In, PunchDirection.Out, PunchDirection.In, PunchDirection.Out], "PDKS molası temsili Out→In çiftiyle korunur");
        punches.Where(p => p.WorkDate == CharacterizationMonth.Date(10)).Last().OccurredAtUtc.ShouldBe(new DateTime(2026, 8, 11, 3, 0, 0, DateTimeKind.Utc));

        var selfService = punches.Where(p => p.WorkDate == SelfServiceDay).ToList();
        selfService.Select(p => p.SourceRef).ShouldBe([selfIn.Id.ToString(), selfOut.Id.ToString()]);
        selfService.ShouldAllBe(p => p.Source == PunchSource.SelfService && p.LocationId == _locationId && !p.IsManual);
    }

    [Fact]
    public async Task Backfill_is_idempotent_and_down_removes_only_backfilled_punches()
    {
        var ct = TestContext.Current.CancellationToken;
        using var tenantScope = TenantContextSetter!.BeginScope(_tenantId);
        await SeedAsync(ct);
        await MigrateToLatestAsync(ct);
        var count = (await LoadPunchesAsync(ct)).Count;
        count.ShouldBeGreaterThan(0);

        using (var scope = ServiceProvider!.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
            await db.Database.ExecuteSqlRawAsync(AttendanceBackfillPunches.BackfillSql, ct);
            db.Punches.Add(Punch.Create(
                _tenantId, _employeeId, new DateTime(2026, 8, 17, 6, 0, 0, DateTimeKind.Utc), CharacterizationMonth.Date(17), PunchDirection.In,
                PunchSource.Terminal, null, null, null, null, null, false, null, null).Value);
            await db.SaveChangesAsync(ct);
        }

        (await LoadPunchesAsync(ct)).Count.ShouldBe(count + 1);

        using (var scope = ServiceProvider!.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AttendanceDbContext>().GetService<IMigrator>().MigrateAsync(SchemaMigration, ct);
        }

        (await LoadPunchesAsync(ct)).ShouldHaveSingleItem().Source.ShouldBe(PunchSource.Terminal);
    }

    private async Task<(SelfCheckIn In, SelfCheckIn Out)> SeedAsync(CancellationToken ct)
    {
        await SeedMixedMonthAsync(_tenantId, _employeeId, ct);
        using var scope = ServiceProvider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        await db.Database.ExecuteSqlRawAsync("INSERT INTO identity.tenants (id, time_zone) VALUES ({0}, {1})", [_tenantId, TenantZone], ct);

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

    private async Task MigrateToLatestAsync(CancellationToken ct)
    {
        using var scope = ServiceProvider!.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AttendanceDbContext>().Database.MigrateAsync(ct);
    }

    private async Task<List<AttendanceDay>> SnapshotDaysAsync(CancellationToken ct)
    {
        using var scope = ServiceProvider!.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AttendanceDbContext>().AttendanceDays.AsNoTracking()
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
        var plan = before.PlannedStart is not null ? new PlannedDay(before.PlannedStart, before.PlannedEnd, before.BreakMinutes) : PlannedDay.Rest;
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

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Application.Punches;
using Sense.Modules.Attendance.Application.Recalculation;
using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Modules.Attendance.Application.ShiftDefinitions;
using Sense.Modules.Attendance.Application.WorkSchedules;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Punches;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Modules.Attendance.Infrastructure.Jobs;
using Sense.Modules.Attendance.Infrastructure.Persistence;
using Sense.Modules.Identity.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Shouldly;
using Xunit;
using static Sense.Modules.Attendance.Tests.ShiftDefinitions.ShiftDefinitionFixtures;

namespace Sense.Modules.Attendance.Tests.PunchPipeline;

/// <summary>
/// Zaman-devam I0, gerçek PostgreSQL: okutma hattı (B1) + vardiya tanımı (B2) + yeniden hesap (B3) birlikte - "Kaydet &amp; Geçmişe Uygula"
/// okutmalı günü yeni toleransla yeniden kurar; okutmasız eski gün (rolling deploy) yeniden hesapta giriş/çıkışını kaybetmez; aynı güne
/// eşzamanlı okutma yazan iki işlem istisna fırlatmaz (tekillik yarışı yoksayılır/yeniden denenir).
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class PunchPipelinePostgresTests : AttendancePostgresFixture
{
    private const string TenantZone = "Europe/Istanbul";

    // Pazartesi (varsayılan çizelge Pzt-Cum 09:00-18:00), gerçek saatten önce - okutmalar ileri tarihli sayılmaz.
    private static readonly DateOnly Date = new(2026, 8, 3);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        var zones = Substitute.For<ITenantTimeZoneLookup>();
        zones.FindTimeZoneIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TenantZone);
        services.AddSingleton(zones);
    }

    [Fact]
    public async Task Save_and_apply_to_past_rebuilds_the_punched_day_with_the_new_tolerance()
    {
        var ct = TestContext.Current.CancellationToken;
        using var tenant = TenantContextSetter!.BeginScope(_tenantId);
        await EnsureDefaultScheduleAsync(ct);
        var definitionId = await AssignDefinitionAsync(lateTolerance: 0, ct);
        await RecordAsync(Utc(9, 8), PunchDirectionDto.In, ct);
        await RecordAsync(Utc(18, 0), PunchDirectionDto.Out, ct);
        (await LoadDayAsync(ct)).Anomalies.HasFlag(AttendanceAnomaly.LateArrival).ShouldBeTrue("tolerans 0: 09:08 geç giriş");

        Guid jobId;
        using (var scope = ServiceProvider!.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<UpdateShiftDefinitionCommand, ShiftDefinitionSaveResultDto>>();
            var saved = await handler.Handle(new UpdateShiftDefinitionCommand(definitionId, Input(lateTolerance: 10), ApplyToPast: true, ApplyFrom: Date), ct);
            await scope.ServiceProvider.GetRequiredService<IAttendanceUnitOfWork>().SaveChangesAsync(ct);
            jobId = saved.Value.RecalculationJobId.ShouldNotBeNull();
        }

        var recalculator = ServiceProvider.GetRequiredService<AttendanceDayRecalculator>();
        await recalculator.RunJobAsync(jobId, ct);

        (await recalculator.GetStatusAsync(_tenantId, jobId, ct)).ShouldNotBeNull().State.ShouldBe(RecalculationJob.Completed);
        var day = await LoadDayAsync(ct);
        day.CheckIn.ShouldBe(new TimeOnly(9, 8));
        day.CheckOut.ShouldBe(new TimeOnly(18, 0));
        day.Anomalies.HasFlag(AttendanceAnomaly.LateArrival).ShouldBeFalse("yeni tanım: 10 dk tolerans okutmalardan yeniden kurulan güne uygulandı");
    }

    [Fact]
    public async Task Recalculation_keeps_check_in_and_out_of_a_day_written_without_punches()
    {
        var ct = TestContext.Current.CancellationToken;
        using var tenant = TenantContextSetter!.BeginScope(_tenantId);
        await EnsureDefaultScheduleAsync(ct);
        using (var scope = ServiceProvider!.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
            db.AttendanceDays.Add(AttendanceDay.Record(
                _tenantId, _employeeId, Date, new TimeOnly(8, 55), new TimeOnly(18, 5), new TimeOnly(9, 0), new TimeOnly(18, 0), 480, 60,
                AttendanceDaySource.Device, actualBreakMinutes: 45));
            await db.SaveChangesAsync(ct);
        }

        var before = await LoadDayAsync(ct);
        var recalculator = ServiceProvider.GetRequiredService<IAttendanceDayRecalculator>();

        (await recalculator.RecalculateNowAsync(_tenantId, Date, Date, [_employeeId], ct)).ShouldBe(1);
        (await recalculator.RecalculateNowAsync(_tenantId, Date, Date, [_employeeId], ct)).ShouldBe(1);

        var after = await LoadDayAsync(ct);
        (after.CheckIn, after.CheckOut, after.ActualBreakMinutes, after.WorkedMinutes, after.NetWorkedMinutes, after.Anomalies, after.Source)
            .ShouldBe((before.CheckIn, before.CheckOut, before.ActualBreakMinutes, before.WorkedMinutes, before.NetWorkedMinutes, before.Anomalies, before.Source));
        var punches = await LoadPunchesAsync(ct);
        punches.Count.ShouldBe(4, "giriş + temsili mola çifti + çıkış; ikinci yeniden hesap tekrar üretmez");
        punches.ShouldAllBe(p => p.SourceRef!.StartsWith("backfill", StringComparison.Ordinal) && p.Reason == PunchDayWriter.LegacyReason);
    }

    [Fact]
    public async Task Concurrent_identical_punches_on_a_new_day_neither_throw_nor_duplicate()
    {
        var ct = TestContext.Current.CancellationToken;
        using var tenant = TenantContextSetter!.BeginScope(_tenantId);
        await EnsureDefaultScheduleAsync(ct);
        var gate = new Rendezvous(parties: 2);
        var input = Input(Utc(9, 0), PunchDirectionDto.In);

        var results = await Task.WhenAll(
            Task.Run(() => RecordRacingAsync(input, gate, ct), ct), Task.Run(() => RecordRacingAsync(input, gate, ct), ct));

        results.ShouldAllBe(r => r.IsSuccess);
        results[0].Value.ShouldBe(results[1].Value, "kaybeden işlem kazananın okutmasını yoksayar");
        (await LoadPunchesAsync(ct)).ShouldHaveSingleItem();
        (await LoadDayAsync(ct)).CheckIn.ShouldBe(new TimeOnly(9, 0));
    }

    [Fact]
    public async Task Concurrent_different_punches_creating_the_same_day_are_both_kept_on_one_day()
    {
        var ct = TestContext.Current.CancellationToken;
        using var tenant = TenantContextSetter!.BeginScope(_tenantId);
        await EnsureDefaultScheduleAsync(ct);
        var gate = new Rendezvous(parties: 2);

        var results = await Task.WhenAll(
            Task.Run(() => RecordRacingAsync(Input(Utc(9, 0), PunchDirectionDto.In), gate, ct), ct),
            Task.Run(() => RecordRacingAsync(Input(Utc(18, 0), PunchDirectionDto.Out), gate, ct), ct));

        results.ShouldAllBe(r => r.IsSuccess);
        (await LoadPunchesAsync(ct)).Count.ShouldBe(2);
        var day = await LoadDayAsync(ct);
        (day.CheckIn, day.CheckOut).ShouldBe((new TimeOnly(9, 0), new TimeOnly(18, 0)), "yarışı kaybeden, kazananın gününü güncelledi");
    }

    /// <summary>İstanbul yerel saati (UTC+3, yaz saati yok) → UTC.</summary>
    private static DateTime Utc(int localHour, int localMinute) =>
        DateTime.SpecifyKind(Date.ToDateTime(new TimeOnly(localHour, localMinute)).AddHours(-3), DateTimeKind.Utc);

    private PunchInput Input(DateTime utc, PunchDirectionDto direction) =>
        new(_tenantId, _employeeId, utc, direction, PunchSourceDto.Terminal, null, null, null, null, null, false, null, null);

    private static ShiftDefinitionInput Input(int lateTolerance) => new(
        "SAB", "Sabah", "#336699", ShiftType.Fixed, Minute(9), Minute(18), false, null, null, null, [], lateTolerance, 0, null, null, null, null, 0);

    private async Task EnsureDefaultScheduleAsync(CancellationToken ct)
    {
        using var scope = ServiceProvider!.CreateScope();
        await scope.ServiceProvider.GetRequiredService<WorkScheduleAssignmentService>().EnsureDefaultScheduleAsync(_tenantId, ct);
        await scope.ServiceProvider.GetRequiredService<AttendanceDbContext>().SaveChangesAsync(ct);
    }

    private async Task<Guid> AssignDefinitionAsync(int lateTolerance, CancellationToken ct)
    {
        using var scope = ServiceProvider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        var definition = ShiftDefinition.Create(_tenantId, Input(lateTolerance).ToValues()).Value;
        var assignment = ShiftAssignment.Create(_tenantId, _employeeId, Date, new TimeOnly(9, 0), new TimeOnly(18, 0), 0, false, null).Value;
        assignment.SetPlan(definition.Id, ShiftDayType.Work);
        db.ShiftDefinitions.Add(definition);
        db.ShiftAssignments.Add(assignment);
        await db.SaveChangesAsync(ct);
        return definition.Id;
    }

    private async Task RecordAsync(DateTime utc, PunchDirectionDto direction, CancellationToken ct)
    {
        using var scope = ServiceProvider!.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<IPunchRecorder>().RecordAsync(Input(utc, direction), ct)).IsSuccess.ShouldBeTrue();
    }

    /// <summary>Kendi kapsamında bir <see cref="PunchRecorder"/>; ilk kaydetme iki işlem de oraya gelene kadar bekler (yarış garanti).</summary>
    private async Task<Sense.Shared.Kernel.Results.Result<Guid>> RecordRacingAsync(PunchInput input, Rendezvous gate, CancellationToken ct)
    {
        using var scope = ServiceProvider!.CreateScope();
        var sp = scope.ServiceProvider;
        var recorder = new PunchRecorder(
            sp.GetRequiredService<IPunchRepository>(), sp.GetRequiredService<IAttendanceDayRepository>(), sp.GetRequiredService<IPlannedDayResolver>(),
            sp.GetRequiredService<IMonthlyTimesheetApprovalRepository>(), sp.GetRequiredService<IShiftSettingsRepository>(),
            sp.GetRequiredService<ITenantLocalClock>(), new RendezvousUnitOfWork(sp.GetRequiredService<IAttendanceUnitOfWork>(), gate),
            sp.GetRequiredService<ITenantContext>(), sp.GetRequiredService<ITenantContextSetter>());
        return await recorder.RecordAsync(input, ct);
    }

    private async Task<AttendanceDay> LoadDayAsync(CancellationToken ct)
    {
        using var scope = ServiceProvider!.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AttendanceDbContext>().AttendanceDays.AsNoTracking()
            .SingleAsync(d => d.EmployeeId == _employeeId && d.Date == Date, ct);
    }

    private async Task<IReadOnlyList<Punch>> LoadPunchesAsync(CancellationToken ct)
    {
        using var scope = ServiceProvider!.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IPunchRepository>().ListForDayAsync(_tenantId, _employeeId, Date, ct);
    }

    /// <summary>İki işlemin ilk kaydetmesini aynı anda başlatır.</summary>
    private sealed class Rendezvous(int parties)
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
        private readonly TaskCompletionSource _all = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public Task ArriveAndWaitAsync(CancellationToken ct)
        {
            if (Interlocked.Increment(ref _arrived) == parties)
            {
                _all.TrySetResult();
            }

            return _all.Task.WaitAsync(Timeout, ct);
        }
    }

    private sealed class RendezvousUnitOfWork(IAttendanceUnitOfWork inner, Rendezvous gate) : IAttendanceUnitOfWork
    {
        private bool _arrived;

        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (!_arrived)
            {
                _arrived = true;
                await gate.ArriveAndWaitAsync(cancellationToken);
            }

            return await inner.SaveChangesAsync(cancellationToken);
        }

        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default) =>
            inner.ExecuteInTransactionAsync(action, cancellationToken);
    }
}

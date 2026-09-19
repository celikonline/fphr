using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Equalization;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Modules.Attendance.Domain.WorkSchedules;
using Sense.Modules.Attendance.Infrastructure;
using Sense.Modules.Attendance.Infrastructure.Persistence;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

/// <summary>
/// Real PostgreSQL (Testcontainers): migration, and Employee.Contracts.EmployeeHired delivered through IEventBus
/// the way Sense.Worker's OutboxProcessor delivers it (handler resolved from DI, tenant scope set by the bus).
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class AttendanceIntegrationTests : TestFixture
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        services.AddModuleDbContext<AttendanceDbContext>(configuration, AttendanceDbContext.SchemaName);
        services.AddAttendanceServices(configuration);
        services.AddModuleHandlers(
            AttendanceDbContext.SchemaName,
            typeof(AttendanceErrors).Assembly,
            typeof(AttendancePermissions).Assembly,
            typeof(AttendanceDbContext).Assembly,
            typeof(IWorkScheduleRepository).Assembly);
    }

    protected override async Task OnDatabaseReadyAsync() =>
        await ServiceProvider!.GetRequiredService<AttendanceDbContext>().Database.MigrateAsync();

    [Fact]
    public void WorkerRegistration_ResolvesEveryAttendanceIntegrationEventHandler()
    {
        var configuration = ServiceProvider!.GetRequiredService<IConfiguration>();
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddLogging();
        services.AddSenseCore(configuration);
        services.AddModuleDbContext<AttendanceDbContext>(configuration, AttendanceDbContext.SchemaName);
        services.AddAttendanceCoreServices(configuration);
        services.AddAttendanceIntegrationEventHandlers();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetServices<IIntegrationEventHandler<EmployeeHired>>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task EmployeeHired_AssignsDefaultScheduleFromHireDate_AndIsIdempotent()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var hireDate = new DateOnly(2026, 3, 1);
        var hired = new EmployeeHired(tenantId, employeeId, hireDate, hireDate);
        var bus = ServiceProvider!.GetRequiredService<IEventBus>();

        await bus.Publish(hired);
        await bus.Publish(hired);

        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();

        var assignment = await db.WorkScheduleAssignments.SingleAsync();
        assignment.EmployeeId.ShouldBe(employeeId);
        assignment.EffectiveFrom.ShouldBe(hireDate);
        assignment.Source.ShouldBe(WorkScheduleAssignmentSource.Hire);

        var schedule = await db.WorkSchedules.SingleAsync();
        schedule.IsDefault.ShouldBeTrue();
        assignment.WorkScheduleId.ShouldBe(schedule.Id);
    }

    [Fact]
    public async Task WorkSchedule_PersistedAndReloaded_RoundTripsDays()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        Guid scheduleId;

        var days = Enum.GetValues<DayOfWeek>()
            .Select(d => d is DayOfWeek.Saturday or DayOfWeek.Sunday
                ? WorkScheduleDay.Off(d)
                : WorkScheduleDay.Working(d, new TimeOnly(9, 0), new TimeOnly(18, 0), new TimeOnly(12, 30), new TimeOnly(13, 30)))
            .ToList();

        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<AttendanceDbContext>();
            var created = WorkSchedule.Create(tenantId, "Satış", days, includeSaturdayWhenFullWeek: true);
            created.IsSuccess.ShouldBeTrue();
            db.WorkSchedules.Add(created.Value);
            await db.SaveChangesAsync();
            scheduleId = created.Value.Id;
        }

        using var read = ServiceProvider.CreateScope();
        var repo = read.ServiceProvider.GetRequiredService<IWorkScheduleRepository>();
        var loaded = (await repo.GetByIdAsync(scheduleId)).Value;
        loaded.Days.Count.ShouldBe(7);
        loaded.IncludeSaturdayWhenFullWeek.ShouldBeTrue();
        loaded.GetDay(DayOfWeek.Monday).IsWorkingDay.ShouldBeTrue();
        loaded.GetDay(DayOfWeek.Monday).Start.ShouldBe(new TimeOnly(9, 0));
        loaded.GetDay(DayOfWeek.Saturday).IsWorkingDay.ShouldBeFalse();
        loaded.WeeklyMinutes.ShouldBe(5 * ((18 - 9) * 60 - 60));
    }

    [Fact]
    public async Task ReadService_FallsBackToDefaultSchedule_UntilEmployeeIsAssigned()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);

        using (var write = ServiceProvider!.CreateScope())
        {
            var assignmentService = write.ServiceProvider.GetRequiredService<Application.WorkSchedules.WorkScheduleAssignmentService>();
            var db = write.ServiceProvider.GetRequiredService<AttendanceDbContext>();
            await assignmentService.EnsureDefaultScheduleAsync(tenantId, TestContext.Current.CancellationToken);
            await db.SaveChangesAsync();
        }

        using var read = ServiceProvider.CreateScope();
        var readService = read.ServiceProvider.GetRequiredService<IWorkScheduleReadService>();
        var monday = new DateOnly(2026, 9, 21);
        var day = await readService.GetForDateAsync(employeeId, monday, TestContext.Current.CancellationToken);
        day.IsWorkingDay.ShouldBeTrue();
        day.Start.ShouldBe(new TimeOnly(9, 0));

        var saturday = monday.AddDays(5);
        (await readService.IsWorkingDayAsync(employeeId, saturday, TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    [Fact]
    public async Task AttendanceDay_PersistedAndReloaded_RoundTripsMultiFlagAnomalies()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        Guid dayId;

        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<AttendanceDbContext>();
            // 09:00 girişte 30dk geç, 19:00 çıkışta geç -> LateArrival + LateLeave + Overwork birlikte.
            var day = AttendanceDay.Record(
                tenantId, employeeId, new DateOnly(2026, 9, 21), new TimeOnly(9, 30), new TimeOnly(19, 0),
                new TimeOnly(9, 0), new TimeOnly(18, 0), plannedMinutes: 8 * 60, breakMinutes: 0, AttendanceDaySource.Manual);
            db.AttendanceDays.Add(day);
            await db.SaveChangesAsync();
            dayId = day.Id;
        }

        using var read = ServiceProvider.CreateScope();
        var repo = read.ServiceProvider.GetRequiredService<IAttendanceDayRepository>();
        var loaded = (await repo.GetByIdAsync(dayId, TestContext.Current.CancellationToken)).Value;
        loaded.Anomalies.HasFlag(AttendanceAnomaly.LateArrival).ShouldBeTrue();
        loaded.Anomalies.HasFlag(AttendanceAnomaly.LateLeave).ShouldBeTrue();
        loaded.Anomalies.HasFlag(AttendanceAnomaly.Overwork).ShouldBeTrue();
        loaded.ResolutionState.ShouldBe(AttendanceResolutionState.Unresolved);

        var listed = await repo.ListForEmployeeAsync(employeeId, new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 21), TestContext.Current.CancellationToken);
        listed.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task AttendanceDay_ReRecordingSameEmployeeAndDate_OverwritesExistingRow()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var date = new DateOnly(2026, 9, 21);
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);

        using var scope = ServiceProvider!.CreateScope();
        var days = scope.ServiceProvider.GetRequiredService<IAttendanceDayRepository>();
        var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();

        var first = AttendanceDay.Record(tenantId, employeeId, date, null, null, new TimeOnly(9, 0), new TimeOnly(17, 0), 8 * 60, 0, AttendanceDaySource.Manual);
        days.Add(first);
        await db.SaveChangesAsync();
        first.Anomalies.ShouldBe(AttendanceAnomaly.NoData);

        var existing = await days.FindByEmployeeAndDateAsync(employeeId, date, TestContext.Current.CancellationToken);
        existing.ShouldNotBeNull();
        existing.UpdateRecord(new TimeOnly(9, 0), new TimeOnly(17, 0), new TimeOnly(9, 0), new TimeOnly(17, 0), 8 * 60, 0, AttendanceDaySource.Import);
        await db.SaveChangesAsync();

        var all = await days.ListForEmployeeAsync(employeeId, date, date, TestContext.Current.CancellationToken);
        all.ShouldHaveSingleItem();
        all[0].Anomalies.ShouldBe(AttendanceAnomaly.None);
        all[0].Source.ShouldBe(AttendanceDaySource.Import);
    }

    /// <summary>Attendance.Contracts port Faz 7 (Payroll) consumes (S-8): worked/missing days and hours aggregated over a calendar month.</summary>
    [Fact]
    public async Task MonthlySummaryReader_AggregatesWorkedAndMissingDays_AndSkipsEmployeesWithNoRecords()
    {
        var tenantId = Guid.NewGuid();
        var withRecords = Guid.NewGuid();
        var withoutRecords = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);

        var workingDaysInMonth = Enumerable.Range(1, DateTime.DaysInMonth(2026, 9))
            .Select(d => new DateOnly(2026, 9, d))
            .Count(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday));

        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<AttendanceDbContext>();
            var assignmentService = write.ServiceProvider.GetRequiredService<Application.WorkSchedules.WorkScheduleAssignmentService>();
            await assignmentService.EnsureDefaultScheduleAsync(tenantId, TestContext.Current.CancellationToken);

            // Normal day: 09:00-18:00 planlanmış (480dk net), tam zamanında.
            db.AttendanceDays.Add(AttendanceDay.Record(
                tenantId, withRecords, new DateOnly(2026, 9, 1), new TimeOnly(9, 0), new TimeOnly(18, 0),
                new TimeOnly(9, 0), new TimeOnly(18, 0), plannedMinutes: 480, breakMinutes: 60, AttendanceDaySource.Manual));
            // Fazla mesai: 09:00-20:00 (600dk net) -> 120dk (2s) fazla.
            db.AttendanceDays.Add(AttendanceDay.Record(
                tenantId, withRecords, new DateOnly(2026, 9, 2), new TimeOnly(9, 0), new TimeOnly(20, 0),
                new TimeOnly(9, 0), new TimeOnly(18, 0), plannedMinutes: 480, breakMinutes: 60, AttendanceDaySource.Manual));

            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var read = ServiceProvider.CreateScope();
        var reader = read.ServiceProvider.GetRequiredService<IAttendanceMonthlySummaryReader>();
        var result = await reader.GetAsync(tenantId, 2026, 9, [withRecords, withoutRecords], TestContext.Current.CancellationToken);

        result.Count.ShouldBe(1); // withoutRecords hiç kayıt bırakmadı - "veri yok" olarak listede yer almaz.
        var summary = result.Single();
        summary.EmployeeId.ShouldBe(withRecords);
        summary.WorkedDays.ShouldBe(2m);
        summary.WorkedHours.ShouldBe(18m); // (480dk + 600dk) / 60
        summary.OvertimeHours.ShouldBe(2m); // yalnız 2. gün 120dk fazla

        summary.MissingDays.ShouldBe((decimal)(workingDaysInMonth - 2));
        summary.IsPeriodLocked.ShouldBeFalse();
    }

    /// <summary>QA: resmi tatil ve onaylı izin günleri, kayıt yoksa "eksik gün" sayılmaz; o günlerde çalışılmışsa çalışılan gün olarak kalır.</summary>
    [Fact]
    public async Task MonthlySummaryReader_HolidaysAndApprovedLeave_AreNotMissingDays()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);

        var workingDaysInMonth = Enumerable.Range(1, DateTime.DaysInMonth(2026, 9))
            .Select(d => new DateOnly(2026, 9, d))
            .Count(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday));

        using var scope = ServiceProvider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        await scope.ServiceProvider.GetRequiredService<Application.WorkSchedules.WorkScheduleAssignmentService>()
            .EnsureDefaultScheduleAsync(tenantId, TestContext.Current.CancellationToken);
        foreach (var date in new[] { new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 2) })
        {
            db.AttendanceDays.Add(AttendanceDay.Record(
                tenantId, employeeId, date, new TimeOnly(9, 0), new TimeOnly(18, 0), new TimeOnly(9, 0), new TimeOnly(18, 0), plannedMinutes: 480, breakMinutes: 60, AttendanceDaySource.Manual));
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var holidays = Substitute.For<Sense.Modules.Organization.Contracts.IHolidayCalendarLookup>();
        var calendarId = Guid.NewGuid();
        holidays.ResolveCalendarAsync(tenantId, null, null, Arg.Any<CancellationToken>()).Returns(new Sense.Modules.Organization.Contracts.HolidayCalendarRef(calendarId, "TR"));
        holidays.GetHolidaysAsync(tenantId, calendarId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<Sense.Modules.Organization.Contracts.UpcomingHoliday> { new(new DateOnly(2026, 9, 3), "Tatil") });
        var leave = Substitute.For<Sense.Modules.Leave.Contracts.IApprovedLeaveDateLookup>();
        leave.GetApprovedLeaveDatesAsync(tenantId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Sense.Modules.Leave.Contracts.ApprovedLeaveDay>
            {
                new(employeeId, new DateOnly(2026, 9, 2)), // izinli ama çalışılmış: çalışılan gün olarak kalır, ayrıca düşülmez
                new(employeeId, new DateOnly(2026, 9, 4)),
                new(employeeId, new DateOnly(2026, 9, 7)),
            });

        var reader = new Infrastructure.Lookups.AttendanceMonthlySummaryReader(
            db, scope.ServiceProvider.GetRequiredService<IWorkScheduleReadService>(), new Application.AttendanceDays.NonWorkingDayResolver(leave, holidays));

        var summary = (await reader.GetAsync(tenantId, 2026, 9, [employeeId], TestContext.Current.CancellationToken)).Single();

        summary.WorkedDays.ShouldBe(2m);
        summary.MissingDays.ShouldBe((decimal)(workingDaysInMonth - 2 - 3)); // 3 Eylül tatil + 4 ve 7 Eylül izin
    }

    // Kart senseik-014 §6 (Puantaj → Bordro Kontratı): equalization is off for this tenant, so the reader only ever
    // has the daily-proxy figure to offer - ProvisionalOvertimeMinutes sums AttendanceDay.OvertimeMinutes, no finalized value.
    [Fact]
    public async Task OvertimePayrollReader_EqualizationDisabled_ReturnsOnlyProvisionalMinutes()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);

        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<AttendanceDbContext>();
            // 09:00-20:00 planlanan 480dk'ya karşı 660dk net (m.68 asgari mola 30dk) -> 180dk fazla mesai.
            db.AttendanceDays.Add(AttendanceDay.Record(
                tenantId, employeeId, new DateOnly(2026, 9, 3), new TimeOnly(9, 0), new TimeOnly(20, 0),
                new TimeOnly(9, 0), new TimeOnly(18, 0), plannedMinutes: 480, breakMinutes: 0, AttendanceDaySource.Manual));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var read = ServiceProvider.CreateScope();
        var reader = read.ServiceProvider.GetRequiredService<IAttendanceOvertimePayrollReader>();
        var result = await reader.GetAsync(tenantId, 2026, 9, [employeeId], TestContext.Current.CancellationToken);

        var line = result.ShouldHaveSingleItem();
        line.EmployeeId.ShouldBe(employeeId);
        line.ProvisionalOvertimeMinutes.ShouldBeGreaterThan(0);
        line.FinalizedOvertimeMinutes.ShouldBeNull();
        line.EqualizationApplies.ShouldBeFalse();
    }

    // Denkleştirme açıkken ve dönem bu ay içinde kapandığında, Payroll'a kesin (TrueUpOvertimeMinutes) değer sunulur.
    [Fact]
    public async Task OvertimePayrollReader_EqualizationEnabledWithClosedPeriodInMonth_ReturnsFinalizedMinutes()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);

        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<AttendanceDbContext>();
            var settings = ShiftSettings.CreateDefault(tenantId);
            settings.UpdateEqualization(enabled: true, periodMonths: 2, anchorMonth: 1, hasWrittenAgreement: false).IsSuccess.ShouldBeTrue();
            db.ShiftSettings.Add(settings);

            var period = EqualizationPeriod.Open(tenantId, employeeId, new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 31), weeksInPeriod: 9);
            period.SetProvisionalTotal(30000).IsSuccess.ShouldBeTrue();
            period.Close(new DateOnly(2026, 10, 31).AddDays(1)).IsSuccess.ShouldBeTrue();
            db.EqualizationPeriods.Add(period);

            // The reader only surfaces an employee that has at least one puantaj record that month (same "absence = no
            // data" contract as IAttendanceMonthlySummaryReader) - a plain day with no overtime still counts.
            db.AttendanceDays.Add(AttendanceDay.Record(
                tenantId, employeeId, new DateOnly(2026, 10, 5), new TimeOnly(9, 0), new TimeOnly(18, 0),
                new TimeOnly(9, 0), new TimeOnly(18, 0), plannedMinutes: 480, breakMinutes: 60, AttendanceDaySource.Manual));

            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var read = ServiceProvider.CreateScope();
        var reader = read.ServiceProvider.GetRequiredService<IAttendanceOvertimePayrollReader>();
        var result = await reader.GetAsync(tenantId, 2026, 10, [employeeId], TestContext.Current.CancellationToken);

        var line = result.ShouldHaveSingleItem();
        line.EqualizationApplies.ShouldBeTrue();
        line.FinalizedOvertimeMinutes.ShouldBe(30000 - (9 * EqualizationPeriod.WeeklyBudgetMinutes));
    }
}

using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Modules.Attendance.Infrastructure.Lookups;
using Sense.Modules.Attendance.Infrastructure.Persistence;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

/// <summary>Gerçek PostgreSQL: IAttendanceDailyKpiReader (zaman-devam Z1.3).</summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class AttendanceDailyKpiReaderTests : AttendancePostgresFixture
{
    private static readonly DateOnly Day1 = new(2026, 9, 21);

    /// <summary>Sabit "bugün" (Day1 + 10) ile okuyucu; gelecek günler devamsız sayılmaz kuralı deterministik test edilir.</summary>
    private static AttendanceDailyKpiReader ReaderAt(AttendanceDbContext db, DateOnly today) =>
        new(db, new FixedLocalClock(today.ToDateTime(new TimeOnly(12, 0))));

    private static async Task AddShiftAsync(AttendanceDbContext db, Guid tenantId, Guid employeeId, DateOnly date, ShiftDayType dayType)
    {
        var shift = ShiftAssignment.Create(tenantId, employeeId, date, new TimeOnly(9, 0), new TimeOnly(18, 0), 0, false, null).Value;
        shift.SetPlan(null, dayType);
        shift.Publish(DateTime.UtcNow);
        db.ShiftAssignments.Add(shift);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Seven_day_totals_fill_missing_days_with_zero()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        using var scope = ServiceProvider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        db.AttendanceDays.Add(AttendanceDay.Record(
            tenantId, employeeId, Day1, new TimeOnly(9, 30), new TimeOnly(18, 0), new TimeOnly(9, 0), new TimeOnly(18, 0), 8 * 60, 0, AttendanceDaySource.Manual));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var reader = ReaderAt(db, Day1.AddDays(10));
        var totals = await reader.DailyTotalsAsync(tenantId, Day1.AddDays(-1), Day1.AddDays(5), [employeeId], TestContext.Current.CancellationToken);

        totals.Count.ShouldBe(7);
        totals.Select(t => t.Date).ShouldBe(Enumerable.Range(-1, 7).Select(Day1.AddDays));
        var day = totals.Single(t => t.Date == Day1);
        (day.Planned, day.Arrived, day.Late, day.Absent).ShouldBe((1, 1, 1, 0));
        totals.Where(t => t.Date != Day1).ShouldAllBe(t => t.Planned == 0 && t.Arrived == 0 && t.Late == 0 && t.Absent == 0 && t.OnLeave == 0);
    }

    [Fact]
    public async Task Employee_totals_count_late_early_overtime_and_shift_days()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        using var scope = ServiceProvider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        // 30 dk geç giriş, 30 dk erken çıkış.
        db.AttendanceDays.Add(AttendanceDay.Record(
            tenantId, employeeId, Day1, new TimeOnly(9, 30), new TimeOnly(17, 30), new TimeOnly(9, 0), new TimeOnly(18, 0), 8 * 60, 0, AttendanceDaySource.Manual));
        foreach (var date in new[] { Day1, Day1.AddDays(1) })
        {
            var shift = ShiftAssignment.Create(tenantId, employeeId, date, new TimeOnly(9, 0), new TimeOnly(18, 0), 0, false, null).Value;
            shift.Publish(DateTime.UtcNow);
            db.ShiftAssignments.Add(shift);
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var reader = ReaderAt(db, Day1.AddDays(10));
        var totals = await reader.EmployeeTotalsAsync(tenantId, Day1, Day1.AddDays(6), [employeeId], TestContext.Current.CancellationToken);

        var row = totals.ShouldHaveSingleItem();
        (row.LateCount, row.LateMinutes, row.EarlyLeaveCount, row.EarlyLeaveMinutes).ShouldBe((1, 30, 1, 30));
        (row.PlannedShiftDays, row.WorkedShiftDays).ShouldBe((2, 1));
    }

    [Fact]
    public async Task Empty_employee_set_returns_empty()
    {
        using var scope = ServiceProvider!.CreateScope();
        var reader = ReaderAt(scope.ServiceProvider.GetRequiredService<AttendanceDbContext>(), Day1);

        (await reader.DailyTotalsAsync(Guid.NewGuid(), Day1, Day1.AddDays(6), [], TestContext.Current.CancellationToken)).ShouldBeEmpty();
        (await reader.EmployeeTotalsAsync(Guid.NewGuid(), Day1, Day1.AddDays(6), [], TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Rest_day_assignments_are_not_planned_shift_days()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        using var scope = ServiceProvider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        await AddShiftAsync(db, tenantId, employeeId, Day1, ShiftDayType.Work);
        await AddShiftAsync(db, tenantId, employeeId, Day1.AddDays(1), ShiftDayType.RestDay);

        var totals = await ReaderAt(db, Day1.AddDays(10)).EmployeeTotalsAsync(tenantId, Day1, Day1.AddDays(6), [employeeId], TestContext.Current.CancellationToken);

        totals.ShouldHaveSingleItem().PlannedShiftDays.ShouldBe(1);
    }

    [Fact]
    public async Task Rate_ignores_weekly_rest_and_public_holiday_rows()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        using var scope = ServiceProvider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        await AddShiftAsync(db, tenantId, employeeId, Day1, ShiftDayType.Work);
        await AddShiftAsync(db, tenantId, employeeId, Day1.AddDays(1), ShiftDayType.WeeklyRest);
        await AddShiftAsync(db, tenantId, employeeId, Day1.AddDays(2), ShiftDayType.PublicHoliday);
        db.AttendanceDays.Add(AttendanceDay.Record(
            tenantId, employeeId, Day1, new TimeOnly(9, 0), new TimeOnly(18, 0), new TimeOnly(9, 0), new TimeOnly(18, 0), 8 * 60, 0, AttendanceDaySource.Manual));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var row = (await ReaderAt(db, Day1.AddDays(10)).EmployeeTotalsAsync(tenantId, Day1, Day1.AddDays(6), [employeeId], TestContext.Current.CancellationToken)).ShouldHaveSingleItem();

        (row.PlannedShiftDays, row.WorkedShiftDays).ShouldBe((1, 1));
    }

    [Fact]
    public async Task Seven_day_totals_planned_excludes_non_work_day_types()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        using var scope = ServiceProvider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        await AddShiftAsync(db, tenantId, employeeId, Day1, ShiftDayType.WeeklyRest);

        var totals = await ReaderAt(db, Day1.AddDays(10)).DailyTotalsAsync(tenantId, Day1, Day1, [employeeId], TestContext.Current.CancellationToken);

        var day = totals.ShouldHaveSingleItem();
        (day.Planned, day.Absent).ShouldBe((0, 0));
    }

    [Fact]
    public async Task Future_planned_days_are_not_counted_absent()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        using var scope = ServiceProvider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        await AddShiftAsync(db, tenantId, employeeId, Day1, ShiftDayType.Work);
        await AddShiftAsync(db, tenantId, employeeId, Day1.AddDays(1), ShiftDayType.Work);

        var totals = await ReaderAt(db, Day1).DailyTotalsAsync(tenantId, Day1, Day1.AddDays(1), [employeeId], TestContext.Current.CancellationToken);

        totals.Select(t => (t.Planned, t.Absent)).ShouldBe([(1, 1), (1, 0)]);
    }

    [Theory]
    [InlineData(9, 30, 9, 0, 30)]
    [InlineData(8, 50, 9, 0, 0)]
    [InlineData(0, 10, 22, 0, 130)]
    [InlineData(21, 50, 22, 0, 0)]
    public void MinutesAfter_wraps_across_midnight(int ah, int am, int rh, int rm, int expected) =>
        AttendanceDailyKpiReader.MinutesAfter(new TimeOnly(ah, am), new TimeOnly(rh, rm)).ShouldBe(expected);
}

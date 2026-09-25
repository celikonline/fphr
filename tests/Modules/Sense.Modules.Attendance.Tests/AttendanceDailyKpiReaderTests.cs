using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Shifts;
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

        var reader = scope.ServiceProvider.GetRequiredService<IAttendanceDailyKpiReader>();
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

        var reader = scope.ServiceProvider.GetRequiredService<IAttendanceDailyKpiReader>();
        var totals = await reader.EmployeeTotalsAsync(tenantId, Day1, Day1.AddDays(6), [employeeId], TestContext.Current.CancellationToken);

        var row = totals.ShouldHaveSingleItem();
        (row.LateCount, row.LateMinutes, row.EarlyLeaveCount, row.EarlyLeaveMinutes).ShouldBe((1, 30, 1, 30));
        (row.PlannedShiftDays, row.WorkedShiftDays).ShouldBe((2, 1));
    }

    [Fact]
    public async Task Empty_employee_set_returns_empty()
    {
        using var scope = ServiceProvider!.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAttendanceDailyKpiReader>();

        (await reader.DailyTotalsAsync(Guid.NewGuid(), Day1, Day1.AddDays(6), [], TestContext.Current.CancellationToken)).ShouldBeEmpty();
        (await reader.EmployeeTotalsAsync(Guid.NewGuid(), Day1, Day1.AddDays(6), [], TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }
}

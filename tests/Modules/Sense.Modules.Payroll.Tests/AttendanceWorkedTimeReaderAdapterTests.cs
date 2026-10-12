using NSubstitute;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Payroll.Infrastructure.Adapters;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>
/// <see cref="AttendanceWorkedTimeReaderAdapter"/> (TB-7, S-8): thin adapter from the batch-shaped
/// IAttendanceMonthlySummaryReader to Payroll's per-employee IWorkedTimeReader port - the only real logic is the
/// decimal-to-int day rounding and the "employee absent from the batch result = no data" mapping.
/// </summary>
public sealed class AttendanceWorkedTimeReaderAdapterTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task GetAsync_RoundsDecimalDaysToNearestWholeDay_WhenAttendanceHasData()
    {
        var employeeId = Guid.NewGuid();
        var summaries = Substitute.For<IAttendanceMonthlySummaryReader>();
        summaries.GetAsync(TenantId, 2026, 3, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == employeeId), null, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AttendanceMonthlySummary>>(
                [new AttendanceMonthlySummary(employeeId, WorkedDays: 18.5m, WorkedHours: 148m, MissingDays: 12.5m, OvertimeHours: 4m, IsPeriodLocked: true)]));

        var adapter = new AttendanceWorkedTimeReaderAdapter(summaries);
        var result = await adapter.GetAsync(TenantId, employeeId, 2026, 3, null, null, CancellationToken.None);

        result.ShouldNotBeNull();
        result.WorkedDays.ShouldBe(19); // 18.5 rounds away from zero
        result.MissingDays.ShouldBe(13); // 12.5 rounds away from zero
        result.OvertimeHours.ShouldBe(4m);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenEmployeeIsAbsentFromTheAttendanceResult()
    {
        var employeeId = Guid.NewGuid();
        var summaries = Substitute.For<IAttendanceMonthlySummaryReader>();
        summaries.GetAsync(TenantId, 2026, 3, Arg.Any<IReadOnlyCollection<Guid>>(), null, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AttendanceMonthlySummary>>([]));

        var adapter = new AttendanceWorkedTimeReaderAdapter(summaries);
        var result = await adapter.GetAsync(TenantId, employeeId, 2026, 3, null, null, CancellationToken.None);

        result.ShouldBeNull();
    }
}

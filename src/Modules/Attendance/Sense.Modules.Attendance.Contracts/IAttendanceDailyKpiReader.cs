namespace Sense.Modules.Attendance.Contracts;

/// <summary>Attendance KPIs for Analytics; callers pass the (already scoped) employee ids, Attendance never resolves org structure.</summary>
public interface IAttendanceDailyKpiReader
{
    /// <summary>Per-day headcounts over the range (dashboard trend, default last 7 days).</summary>
    Task<IReadOnlyList<DailyAttendanceTotalsDto>> DailyTotalsAsync(
        Guid tenantId, DateOnly from, DateOnly to, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct);

    /// <summary>Per-employee late/early counts and minutes, overtime minutes and planned/worked shift days.</summary>
    Task<IReadOnlyList<EmployeeAttendancePeriodDto>> EmployeeTotalsAsync(
        Guid tenantId, DateOnly from, DateOnly to, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct);
}

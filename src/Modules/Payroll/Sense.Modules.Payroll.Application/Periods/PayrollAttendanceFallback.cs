using Sense.Modules.Payroll.Application.Ports;

namespace Sense.Modules.Payroll.Application.Periods;

/// <summary>Shared calculation/change-detection fallback. Leave-only data does not prove that all other days were absent.</summary>
public static class PayrollAttendanceFallback
{
    public static async Task<WorkedTimeSummary> ResolveAsync(
        IAbsenceDaysReader absences, WorkedTimeSummary? attendance, Guid tenantId, Guid employeeId,
        int year, int month, int paidDays, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        if (attendance is { IsEstimated: false }) return attendance;
        var missing = (from is not null || to is not null
            ? await absences.GetUnexcusedDaysAsync(tenantId, employeeId, year, month, from, to, cancellationToken)
            : await absences.GetUnexcusedDaysAsync(tenantId, employeeId, year, month, cancellationToken)) ?? 0;
        var expected = attendance?.ExpectedWorkedDays ?? DateTime.DaysInMonth(year, month);
        var worked = Math.Max(0, expected - missing);
        if (paidDays < 30) worked = Math.Min(worked, paidDays);
        return (attendance ?? new WorkedTimeSummary(0, 0m, 0m)) with
        {
            WorkedDays = worked, MissingDays = missing, NormalHoursShortfall = null,
            IsEstimated = true, ExpectedWorkedDays = expected,
        };
    }
}

using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Payroll.Application.Ports;

namespace Sense.Modules.Payroll.Infrastructure.Adapters;

/// <summary>
/// Adapts Attendance.Contracts.IAttendanceMonthlySummaryReader (S-8, TB-7 - PO-approved tur 3 sözleşmesi) to
/// Payroll's IWorkedTimeReader port; replaces <see cref="NullWorkedTimeReader"/> now that Attendance's monthly
/// summary exists. Mirrors Leave.Infrastructure.WorkScheduleReaderAdapter's role (adapt one cross-module Contracts
/// reader to this module's own narrow port).
/// </summary>
public sealed class AttendanceWorkedTimeReaderAdapter(IAttendanceMonthlySummaryReader summaries) : IWorkedTimeReader
{
    public async Task<WorkedTimeSummary?> GetAsync(Guid tenantId, Guid employeeId, int year, int month,
        DateOnly? from = null, DateOnly? to = null, CancellationToken cancellationToken = default)
    {
        // The Attendance port is batch-shaped (Payroll's own period run calls this per employee in a loop today -
        // see PayrollPeriodCalculationService.CalculateOneAsync - so this always asks for a one-employee batch).
        // Absence from the result list means Attendance has no AttendanceDay rows for this employee/month at all
        // ("veri yok", not "0 gün" - see AttendanceMonthlySummary's own doc), which this maps to null exactly like
        // NullWorkedTimeReader, so the caller falls back to the full-calendar-month assumption.
        var results = await summaries.GetAsync(tenantId, year, month, [employeeId], from, to, cancellationToken);
        var match = results.FirstOrDefault(r => r.EmployeeId == employeeId);
        if (match is null)
        {
            return null;
        }

        // AttendanceMonthlySummary keeps WorkedDays/MissingDays as decimal (half-day granularity is a documented
        // Attendance concept); PayrollCalculationInput.WorkedDays is int (this slice's calculator has no half-day
        // pro-ration - see its own "tam ay varsayımı" TODO), so this rounds to the nearest whole day rather than
        // truncating (a worked half-day should not simply disappear from the count).
        var workedDays = (int)Math.Round(match.WorkedDays, MidpointRounding.AwayFromZero);
        var missingDays = (int)Math.Round(match.MissingDays, MidpointRounding.AwayFromZero);
        return new WorkedTimeSummary(workedDays, missingDays, match.OvertimeHours, match.PublicHolidayDaysWorked, match.WeeklyRestDaysWorked,
            match.NormalHoursShortfall, match.IsTimesheetApproved, match.WorkedHours);
    }
}

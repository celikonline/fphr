using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Contracts.Events;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.AttendanceDays;

/// <summary>Haftalık puantaj tamamlanma durumu (11.3.6): a projection, not a stored row - recomputed from AttendanceDay each time.</summary>
public enum TimesheetWeekStatus
{
    /// <summary>Not every working day of the week has a record yet (nothing imported/recorded, or partially so) and no unresolved anomaly either.</summary>
    Open = 0,

    /// <summary>At least one recorded day still has an unresolved anomaly.</summary>
    HasIssues = 1,

    /// <summary>Every working day of the week is recorded and none has an unresolved anomaly.</summary>
    Completed = 2,
}

public sealed record TimesheetWeekSummaryDto(
    Guid EmployeeId, DateOnly WeekStart, DateOnly WeekEnd, TimesheetWeekStatus Status, int WorkingDayCount, int RecordedWorkingDayCount, int UnresolvedDayCount);

internal static class TimesheetWeekEvaluator
{
    /// <summary>Monday of the calendar week containing <paramref name="date"/> (ISO week start).</summary>
    public static DateOnly MondayOf(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    public static async Task<TimesheetWeekSummaryDto> EvaluateAsync(
        IAttendanceDayRepository days, IWorkScheduleReadService schedules, Guid employeeId, DateOnly weekStart, CancellationToken cancellationToken)
    {
        var normalizedStart = MondayOf(weekStart);
        var weekEnd = normalizedStart.AddDays(6);
        var workingDays = await schedules.GetWorkingDaysAsync(employeeId, normalizedStart, weekEnd, cancellationToken);
        var records = await days.ListForEmployeeAsync(employeeId, normalizedStart, weekEnd, cancellationToken);

        var workingDaySet = workingDays.ToHashSet();
        var recordedWorkingDayCount = records.Count(d => workingDaySet.Contains(d.Date));
        var unresolvedDayCount = records.Count(d => d.ResolutionState == AttendanceResolutionState.Unresolved);

        var status = unresolvedDayCount > 0
            ? TimesheetWeekStatus.HasIssues
            : recordedWorkingDayCount < workingDaySet.Count
                ? TimesheetWeekStatus.Open
                : TimesheetWeekStatus.Completed;

        return new TimesheetWeekSummaryDto(employeeId, normalizedStart, weekEnd, status, workingDaySet.Count, recordedWorkingDayCount, unresolvedDayCount);
    }

    /// <summary>Called after a resolve action commits: publishes TimesheetWeekCompleted if that action happened to clear the last open day of its week.</summary>
    public static async Task PublishIfCompletedAsync(
        IAttendanceDayRepository days, IWorkScheduleReadService schedules, IIntegrationEventOutbox outbox, Guid tenantId, Guid employeeId, DateOnly date,
        CancellationToken cancellationToken)
    {
        var summary = await EvaluateAsync(days, schedules, employeeId, MondayOf(date), cancellationToken);
        if (summary.Status == TimesheetWeekStatus.Completed)
        {
            outbox.Enqueue(new TimesheetWeekCompleted(tenantId, employeeId, summary.WeekStart));
        }
    }
}

/// <summary>"28 Ekim haftası" haftalık ızgara başlığı (11.3.6): the week's completion status for the timesheet screen.</summary>
[RequiresPermission("attendance.timesheet.view")]
public sealed record GetTimesheetWeekSummaryQuery(Guid EmployeeId, DateOnly WeekStart) : IQuery<TimesheetWeekSummaryDto>;

public sealed class GetTimesheetWeekSummaryHandler(IAttendanceDayRepository days, IWorkScheduleReadService schedules)
    : IQueryHandler<GetTimesheetWeekSummaryQuery, TimesheetWeekSummaryDto>
{
    public async Task<Result<TimesheetWeekSummaryDto>> Handle(GetTimesheetWeekSummaryQuery query, CancellationToken cancellationToken) =>
        await TimesheetWeekEvaluator.EvaluateAsync(days, schedules, query.EmployeeId, query.WeekStart, cancellationToken);
}

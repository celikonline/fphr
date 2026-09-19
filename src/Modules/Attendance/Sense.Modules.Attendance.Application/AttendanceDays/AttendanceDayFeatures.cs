using FluentValidation;
using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.AttendanceDays;

public sealed record AttendanceDayDto(
    Guid Id, Guid EmployeeId, DateOnly Date, TimeOnly? CheckIn, TimeOnly? CheckOut, TimeOnly? PlannedStart, TimeOnly? PlannedEnd,
    int PlannedMinutes, int WorkedMinutes, AttendanceAnomaly Anomalies, AttendanceResolutionState ResolutionState, AttendanceDaySource Source,
    IReadOnlyList<AnomalyResolutionAction> AllowedActions);

internal static class AttendanceDayMapper
{
    public static AttendanceDayDto ToDto(AttendanceDay day) =>
        new(day.Id, day.EmployeeId, day.Date, day.CheckIn, day.CheckOut, day.PlannedStart, day.PlannedEnd,
            day.PlannedMinutes, day.OverrideWorkedMinutes ?? day.WorkedMinutes, day.Anomalies, day.ResolutionState, day.Source,
            day.ResolutionState == AttendanceResolutionState.Unresolved
                ? AnomalyResolutionAdvisor.AllowedActions(day.Anomalies).ToList()
                : []);
}

/// <summary>
/// Resolves the WorkSchedule day in effect on <paramref name="date"/> into the (PlannedStart, PlannedEnd, PlannedMinutes,
/// BreakMinutes) tuple <see cref="AttendanceDay"/> snapshots at record time.
/// </summary>
internal static class PlannedDayResolver
{
    public static async Task<(TimeOnly? Start, TimeOnly? End, int Minutes, int BreakMinutes)> ResolveAsync(
        IWorkScheduleReadService schedules, Guid employeeId, DateOnly date, CancellationToken cancellationToken)
    {
        var day = await schedules.GetForDateAsync(employeeId, date, cancellationToken);
        if (!day.IsWorkingDay || day.Start is null || day.End is null)
        {
            return (null, null, 0, 0);
        }

        var breakMinutes = day.BreakStart is not null && day.BreakEnd is not null
            ? (int)(day.BreakEnd.Value.ToTimeSpan() - day.BreakStart.Value.ToTimeSpan()).TotalMinutes
            : 0;
        var plannedMinutes = Math.Max((int)(day.End.Value.ToTimeSpan() - day.Start.Value.ToTimeSpan()).TotalMinutes - breakMinutes, 0);
        return (day.Start, day.End, plannedMinutes, breakMinutes);
    }
}

/// <summary>Manual timesheet entry/correction (11.3.6 "Çalışılan saat"); PDKS import (package A2b) will reuse the same upsert path.</summary>
[RequiresPermission("attendance.timesheet.edit")]
public sealed record RecordAttendanceCommand(Guid EmployeeId, DateOnly Date, TimeOnly? CheckIn, TimeOnly? CheckOut) : ICommand<Guid>;

public sealed class RecordAttendanceValidator : AbstractValidator<RecordAttendanceCommand>
{
    public RecordAttendanceValidator() =>
        RuleFor(x => x).Must(x => x.CheckIn is null || x.CheckOut is null || x.CheckOut > x.CheckIn)
            .WithMessage(AttendanceDomainErrors.DayCheckOutBeforeCheckIn);
}

public sealed class RecordAttendanceHandler(
    IAttendanceDayRepository days, IWorkScheduleReadService schedules, ITenantContext tenant, IDataScopeService dataScope, ITenantLocalClock localClock)
    : ICommandHandler<RecordAttendanceCommand, Guid>
{
    public async Task<Result<Guid>> Handle(RecordAttendanceCommand command, CancellationToken cancellationToken)
    {
        if (!await dataScope.CanAccessEmployeeAsync(AttendancePermissions.TimesheetEdit.Key, command.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(AttendanceErrors.EmployeeNotInScope);
        }

        // A day that has not started yet (in the tenant's own time zone) cannot already have worked hours.
        var localToday = DateOnly.FromDateTime((await localClock.NowAsync(tenant.TenantId, cancellationToken)).Local);
        if (command.Date > localToday)
        {
            return Error.Validation(AttendanceErrors.DateInFuture);
        }

        var result = await AttendanceDayUpsert.UpsertAsync(
            days, schedules, tenant.TenantId, command.EmployeeId, command.Date, command.CheckIn, command.CheckOut,
            AttendanceDaySource.Manual, overnight: false, cancellationToken);
        return result.IsFailure ? result.Error : result.Value.Id;
    }
}

/// <summary>Haftalık puantaj görünümü (11.3.6): an employee's recorded days across a date range.</summary>
[RequiresPermission("attendance.timesheet.view")]
public sealed record ListAttendanceDaysQuery(Guid EmployeeId, DateOnly From, DateOnly To) : IQuery<IReadOnlyList<AttendanceDayDto>>;

public sealed class ListAttendanceDaysHandler(IAttendanceDayRepository days, IDataScopeService dataScope)
    : IQueryHandler<ListAttendanceDaysQuery, IReadOnlyList<AttendanceDayDto>>
{
    public async Task<Result<IReadOnlyList<AttendanceDayDto>>> Handle(ListAttendanceDaysQuery query, CancellationToken cancellationToken)
    {
        // [RequiresPermission] only proves the caller holds the permission, not that this employee is inside their data scope.
        if (!await dataScope.CanAccessEmployeeAsync(AttendancePermissions.TimesheetView.Key, query.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(AttendanceErrors.EmployeeNotInScope);
        }

        return (await days.ListForEmployeeAsync(query.EmployeeId, query.From, query.To, cancellationToken)).Select(AttendanceDayMapper.ToDto).ToList();
    }
}

/// <summary>"Puantajım" (11.3.6 self-servis): the caller's own recorded days.</summary>
[RequiresPermission("attendance.my.timesheet")]
public sealed record GetMyAttendanceDaysQuery(DateOnly From, DateOnly To) : IQuery<IReadOnlyList<AttendanceDayDto>>;

public sealed class GetMyAttendanceDaysHandler(IAttendanceDayRepository days, ICurrentUser currentUser)
    : IQueryHandler<GetMyAttendanceDaysQuery, IReadOnlyList<AttendanceDayDto>>
{
    public async Task<Result<IReadOnlyList<AttendanceDayDto>>> Handle(GetMyAttendanceDaysQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is null)
        {
            return Error.NotFound(AttendanceErrors.EmployeeNotFound);
        }

        var list = await days.ListForEmployeeAsync(currentUser.EmployeeId.Value, query.From, query.To, cancellationToken);
        return list.Select(AttendanceDayMapper.ToDto).ToList();
    }
}

/// <summary>Puantajım özet kartları (package A5, 11.3.6 "toplam çalışma günü/saati"): totals over a period, from the caller's own AttendanceDay rows.</summary>
public sealed record MyTimesheetSummaryDto(int WorkedDays, int WorkedMinutes);

[RequiresPermission("attendance.my.timesheet")]
public sealed record GetMyTimesheetSummaryQuery(DateOnly From, DateOnly To) : IQuery<MyTimesheetSummaryDto>;

public sealed class GetMyTimesheetSummaryHandler(IAttendanceDayRepository days, ICurrentUser currentUser) : IQueryHandler<GetMyTimesheetSummaryQuery, MyTimesheetSummaryDto>
{
    public async Task<Result<MyTimesheetSummaryDto>> Handle(GetMyTimesheetSummaryQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(AttendanceErrors.EmployeeNotFound);
        }

        var list = await days.ListForEmployeeAsync(employeeId, query.From, query.To, cancellationToken);
        var workedMinutes = list.Sum(d => d.OverrideWorkedMinutes ?? d.WorkedMinutes);
        var workedDays = list.Count(d => (d.OverrideWorkedMinutes ?? d.WorkedMinutes) > 0);
        return new MyTimesheetSummaryDto(workedDays, workedMinutes);
    }
}

using FluentValidation;
using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Modules.Attendance.Application.ShiftDefinitions;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Shifts;
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
/// The plan one employee-day is calculated against (snapshot into <see cref="AttendanceDay"/> at record time).
/// <see cref="Start"/>/<see cref="End"/> null = not a working day (schedule off day or a non-<see cref="ShiftDayType.Work"/> assignment).
/// <see cref="Rules"/> is the <see cref="ShiftDefinition"/> rule set (tolerances, overtime thresholds, bonus) when the day is
/// planned through one. <see cref="IsAmbiguous"/>: a dynamic shift had two equally close candidates (spec §9, Incalculable adayı).
/// </summary>
public sealed record PlannedDay(
    TimeOnly? Start,
    TimeOnly? End,
    int PlannedMinutes,
    int BreakMinutes,
    bool StartsPreviousDay = false,
    ShiftRules? Rules = null,
    Guid? ShiftDefinitionId = null,
    ShiftDayType DayType = ShiftDayType.Work,
    bool IsAmbiguous = false)
{
    public bool IsWorkingDay => Start is not null && End is not null;

    public static PlannedDay NonWorking(ShiftDayType dayType = ShiftDayType.Work) => new(null, null, 0, 0, DayType: dayType);
}

/// <summary>Plan çözümleme (K2): see <see cref="PlannedDayResolver"/>.</summary>
public interface IPlannedDayResolver
{
    /// <summary><paramref name="firstIn"/> is the day's first check-in, needed by flexible (K1) and dynamic (K1) shifts.</summary>
    Task<PlannedDay> ResolveAsync(Guid employeeId, DateOnly date, TimeOnly? firstIn = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// K2 önceliği: o günün <see cref="ShiftAssignment"/>'ı varsa o (gün türü <see cref="ShiftDayType.Work"/> değilse saatleri yok
/// sayılır; <see cref="ShiftAssignment.ShiftDefinitionId"/> varsa tanımın saatleri + kural seti, dinamik tanımda ilk girişe en yakın
/// dinamik tanım; tanımsız eski atamada satırın kendi saatleri) → yoksa <see cref="IWorkScheduleReadService"/> günü. Uses the
/// tenant-filtered repositories, so it runs inside a tenant context.
/// </summary>
public sealed class PlannedDayResolver(
    IWorkScheduleReadService schedules, IShiftAssignmentRepository assignments, IShiftDefinitionRepository definitions) : IPlannedDayResolver
{
    public async Task<PlannedDay> ResolveAsync(Guid employeeId, DateOnly date, TimeOnly? firstIn = null, CancellationToken cancellationToken = default)
    {
        var assignment = await assignments.FindByEmployeeAndDateAsync(employeeId, date, cancellationToken).ConfigureAwait(false);
        if (assignment is null)
        {
            return await FromScheduleAsync(schedules, employeeId, date, cancellationToken).ConfigureAwait(false);
        }

        if (assignment.DayType != ShiftDayType.Work)
        {
            return PlannedDay.NonWorking(assignment.DayType);
        }

        return await FromDefinitionAsync(assignment.ShiftDefinitionId, firstIn, cancellationToken).ConfigureAwait(false)
            ?? ShiftRuleEvaluator.FromAssignment(assignment);
    }

    /// <summary>The WorkSchedule day in effect on <paramref name="date"/> (pre-Z0.2 behavior, still the K2 fallback).</summary>
    public static async Task<PlannedDay> FromScheduleAsync(
        IWorkScheduleReadService schedules, Guid employeeId, DateOnly date, CancellationToken cancellationToken)
    {
        var day = await schedules.GetForDateAsync(employeeId, date, cancellationToken).ConfigureAwait(false);
        if (!day.IsWorkingDay || day.Start is null || day.End is null)
        {
            return PlannedDay.NonWorking();
        }

        var breakMinutes = day.BreakStart is not null && day.BreakEnd is not null
            ? (int)(day.BreakEnd.Value.ToTimeSpan() - day.BreakStart.Value.ToTimeSpan()).TotalMinutes
            : 0;
        var plannedMinutes = Math.Max((int)(day.End.Value.ToTimeSpan() - day.Start.Value.ToTimeSpan()).TotalMinutes - breakMinutes, 0);
        return new PlannedDay(day.Start, day.End, plannedMinutes, breakMinutes);
    }

    /// <summary>Null when the assignment has no definition or it no longer exists (then the assignment's own hours apply).</summary>
    private async Task<PlannedDay?> FromDefinitionAsync(Guid? definitionId, TimeOnly? firstIn, CancellationToken cancellationToken)
    {
        if (definitionId is null)
        {
            return null;
        }

        var found = await definitions.GetByIdAsync(definitionId.Value, cancellationToken).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return null;
        }

        var definition = found.Value;
        if (definition.Type != ShiftType.Dynamic || firstIn is not { } first)
        {
            return ShiftRuleEvaluator.Plan(definition, firstIn);
        }

        var match = DynamicShiftMatcher.Match(await definitions.ListAsync(includeInactive: false, cancellationToken).ConfigureAwait(false), first);
        return match.IsAmbiguous
            ? ShiftRuleEvaluator.Plan(definition, firstIn) with { IsAmbiguous = true }
            : ShiftRuleEvaluator.Plan(match.Definition ?? definition, firstIn);
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
    IAttendanceDayRepository days, IPlannedDayResolver planner, ITenantContext tenant, IDataScopeService dataScope, ITenantLocalClock localClock)
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
            days, planner, tenant.TenantId, command.EmployeeId, command.Date, command.CheckIn, command.CheckOut,
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

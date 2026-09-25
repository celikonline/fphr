using FluentValidation;
using Sense.Modules.Attendance.Application.Punches;
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
    IReadOnlyList<AnomalyResolutionAction> AllowedActions, IncalculableReason? IncalculableReason = null);

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
/// The plan one employee-day is calculated against (snapshot into <see cref="AttendanceDay"/> at record time) - the single plan type of
/// the punch → day pipeline (zaman-devam I0: B1's okutma penceresi and B2's kural seti share it).
/// <see cref="Start"/>/<see cref="End"/> null = not a working day (schedule off day or a non-<see cref="ShiftDayType.Work"/> assignment);
/// "no plan at all" (neither assignment nor schedule) is a <c>null</c> <see cref="PlannedDay"/>, never an instance.
/// <see cref="End"/> ≤ <see cref="Start"/> is a night shift; <see cref="StartsPreviousDay"/> says it starts on the previous calendar day.
/// <see cref="Rules"/> is the <see cref="ShiftDefinition"/> rule set (tolerances, overtime thresholds, bonus) when the day is
/// planned through one. <see cref="IsAmbiguous"/>: a dynamic shift had two equally close candidates (K1) - the day is Incalculable.
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

    /// <summary><paramref name="workDate"/> gününün vardiya başlangıç/bitiş anları (yerel duvar saati); çalışma günü değilse null (spec §5.1 penceresi).</summary>
    public (DateTime Start, DateTime End)? ShiftBounds(DateOnly workDate)
    {
        if (Start is not { } start || End is not { } end)
        {
            return null;
        }

        var shiftStart = workDate.ToDateTime(start).AddDays(StartsPreviousDay ? -1 : 0);
        var shiftEnd = DateOnly.FromDateTime(shiftStart).ToDateTime(end).AddDays(end <= start ? 1 : 0);
        return (shiftStart, shiftEnd);
    }
}

/// <summary>Plan çözümleme (K2): see <see cref="PlannedDayResolver"/>.</summary>
public interface IPlannedDayResolver
{
    /// <summary>
    /// <paramref name="firstIn"/> is the day's first check-in, needed by flexible (K1) and dynamic (K1) shifts. <c>null</c> = no plan at all
    /// (no shift assignment and no work schedule, not even the tenant default) - punches on such a day are Incalculable (spec §5.1).
    /// </summary>
    Task<PlannedDay?> ResolveAsync(Guid employeeId, DateOnly date, TimeOnly? firstIn = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// K2 önceliği: o günün <see cref="ShiftAssignment"/>'ı varsa o (gün türü <see cref="ShiftDayType.Work"/> değilse saatleri yok
/// sayılır; <see cref="ShiftAssignment.ShiftDefinitionId"/> varsa tanımın saatleri + kural seti, dinamik tanımda ilk girişe en yakın
/// dinamik tanım; tanımsız eski atamada satırın kendi saatleri) → yoksa <see cref="IWorkScheduleReadService"/> günü → ikisi de yoksa
/// <c>null</c> (plansız). Uses the tenant-filtered repositories, so it runs inside a tenant context.
/// </summary>
public sealed class PlannedDayResolver(
    IWorkScheduleReadService schedules, IShiftAssignmentRepository assignments, IShiftDefinitionRepository definitions) : IPlannedDayResolver
{
    public async Task<PlannedDay?> ResolveAsync(Guid employeeId, DateOnly date, TimeOnly? firstIn = null, CancellationToken cancellationToken = default)
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

    /// <summary>
    /// The WorkSchedule day in effect on <paramref name="date"/> (pre-Z0.2 behavior, still the K2 fallback); null when the employee has no
    /// schedule at all (<see cref="EmployeeScheduleDay.HasSchedule"/>).
    /// </summary>
    public static async Task<PlannedDay?> FromScheduleAsync(
        IWorkScheduleReadService schedules, Guid employeeId, DateOnly date, CancellationToken cancellationToken)
    {
        // Null only from a port stub that knows no schedule for the date - treated like "no schedule at all".
        var day = await schedules.GetForDateAsync(employeeId, date, cancellationToken).ConfigureAwait(false);
        if (day is null || !day.HasSchedule)
        {
            return null;
        }

        if (!day.IsWorkingDay || day.Start is null || day.End is null)
        {
            return PlannedDay.NonWorking();
        }

        var breakMinutes = day.BreakStart is not null && day.BreakEnd is not null
            ? (int)(day.BreakEnd.Value.ToTimeSpan() - day.BreakStart.Value.ToTimeSpan()).TotalMinutes
            : 0;
        var plannedMinutes = Math.Max((int)(day.End.Value.ToTimeSpan() - day.Start.Value.ToTimeSpan()).TotalMinutes - breakMinutes, 0);
        return new PlannedDay(Start: day.Start, End: day.End, PlannedMinutes: plannedMinutes, BreakMinutes: breakMinutes);
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
    IAttendanceDayRepository days, IPlannedDayResolver planner, ITenantContext tenant, IDataScopeService dataScope, ITenantLocalClock localClock,
    IPunchRepository punches, ICurrentUser currentUser, IMonthlyTimesheetApprovalRepository approvals, IAttendanceUnitOfWork unitOfWork)
    : ICommandHandler<RecordAttendanceCommand, Guid>
{
    /// <summary>Elle kayıt günün önceki okutmalarının yerini aldığında silinen okutmalara yazılan gerekçe (Z0.1: elle kayıt okutmaya dönüşür).</summary>
    public const string ReplacedReason = "Elle puantaj kaydıyla değiştirildi";

    public async Task<Result<Guid>> Handle(RecordAttendanceCommand command, CancellationToken cancellationToken)
    {
        if (!await dataScope.CanAccessEmployeeAsync(AttendancePermissions.TimesheetEdit.Key, command.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(AttendanceErrors.EmployeeNotInScope);
        }

        // A day that has not started yet (in the tenant's own time zone) cannot already have worked hours.
        var now = await localClock.NowAsync(tenant.TenantId, cancellationToken);
        if (command.Date > DateOnly.FromDateTime(now.Local))
        {
            return Error.Validation(AttendanceErrors.DateInFuture);
        }

        // Z0.1: elle kayıt Source=Manual okutmalara dönüşür; gün bu okutmalardan ve K2 planından (IPlannedDayResolver, vardiya tanımı
        // kuralları dahil) türetilir. ShiftSettings limitleri bugünkü gibi uygulanmaz.
        var zone = await localClock.GetTimeZoneAsync(tenant.TenantId, cancellationToken);
        var writer = new PunchDayWriter(tenant.TenantId, days, punches, planner, approvals, zone, now.Utc);
        var result = await writer.ReplaceDayAsync(
            command.EmployeeId, command.Date, command.CheckIn, command.CheckOut, AttendanceDaySource.Manual, currentUser.UserId, ReplacedReason,
            limits: null, cancellationToken);
        if (result.IsFailure)
        {
            return result.Error;
        }

        // Kaydetme yazıcıda: eşzamanlı yazma çakışması (kayıp güncelleme / aynı gün) yeniden türetilip denenir.
        var saved = await writer.SaveAsync(unitOfWork, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        // Yarışta gün başka işlemde oluştuysa kalıcı satırın id'si.
        return (await writer.FindDayAsync(command.EmployeeId, command.Date, cancellationToken) ?? result.Value).Id;
    }
}

/// <summary>Haftalık puantaj görünümü (11.3.6): an employee's recorded days across a date range.</summary>
[RequiresPermission("attendance.timesheet.view")]
public sealed record ListAttendanceDaysQuery(Guid EmployeeId, DateOnly From, DateOnly To) : IQuery<IReadOnlyList<AttendanceDayDto>>;

public sealed class ListAttendanceDaysHandler(
    IAttendanceDayRepository days, IDataScopeService dataScope, IPunchRepository punches, ITenantContext tenant, IPlannedDayResolver planner)
    : IQueryHandler<ListAttendanceDaysQuery, IReadOnlyList<AttendanceDayDto>>
{
    public async Task<Result<IReadOnlyList<AttendanceDayDto>>> Handle(ListAttendanceDaysQuery query, CancellationToken cancellationToken)
    {
        // [RequiresPermission] only proves the caller holds the permission, not that this employee is inside their data scope.
        if (!await dataScope.CanAccessEmployeeAsync(AttendancePermissions.TimesheetView.Key, query.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(AttendanceErrors.EmployeeNotInScope);
        }

        var list = await days.ListForEmployeeAsync(query.EmployeeId, query.From, query.To, cancellationToken);
        var dtos = new List<AttendanceDayDto>(list.Count);
        foreach (var day in list)
        {
            var dto = AttendanceDayMapper.ToDto(day);
            dtos.Add((day.Anomalies & AttendanceAnomaly.Incalculable) == 0
                ? dto
                : dto with { IncalculableReason = await ExplainAsync(day, cancellationToken) });
        }

        return dtos;
    }

    /// <summary>
    /// Hesaplanamayan günün nedeni (ör. elle + cihaz kanal çakışması, plansız okutma) günün okutmaları ve bugünkü K2 planından sorgu anında
    /// türetilir - kalıcı alan yok. Okutmalar planla hesaplanabilir çıkıyorsa gün dinamik vardiya eşitliği ya da (ikisi de değilse) kalıcı
    /// olmayan gece vardiyası çakışması yüzünden işaretlenmiştir.
    /// </summary>
    private async Task<IncalculableReason> ExplainAsync(AttendanceDay day, CancellationToken cancellationToken)
    {
        var dayPunches = await punches.ListForDayAsync(tenant.TenantId, day.EmployeeId, day.Date, cancellationToken);
        var plan = await planner.ResolveAsync(day.EmployeeId, day.Date, day.CheckIn, cancellationToken);
        var reason = DayFromPunches.Build(dayPunches, plan).Reason;
        if (reason != IncalculableReason.None)
        {
            return reason;
        }

        return plan?.IsAmbiguous == true ? IncalculableReason.AmbiguousShift : IncalculableReason.NightShiftOverlap;
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

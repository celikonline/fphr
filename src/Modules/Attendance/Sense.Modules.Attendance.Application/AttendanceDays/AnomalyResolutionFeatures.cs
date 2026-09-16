using FluentValidation;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Leave.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.AttendanceDays;

internal static class LinkedEntityTypes
{
    public const string OvertimeRequest = "OvertimeRequest";
    public const string LeaveRequest = "LeaveRequest";
}

public sealed record AnomalyResolutionDto(
    Guid Id, Guid AttendanceDayId, AttendanceAnomaly Anomaly, AnomalyResolutionAction Action,
    string? LinkedEntityType, Guid? LinkedEntityId, Guid? ResolvedByUserId, DateTime ResolvedAt);

internal static class AnomalyResolutionMapper
{
    public static AnomalyResolutionDto ToDto(AnomalyResolution r) =>
        new(r.Id, r.AttendanceDayId, r.Anomaly, r.Action, r.LinkedEntityType, r.LinkedEntityId, r.ResolvedByUserId, r.ResolvedAt);
}

/// <summary>"Çalışılan saat" resolution (11.3.6 Az çalışma) and the manual-correction path for missing/unplanned-data anomalies with no Overwork/Underwork flag.</summary>
[RequiresPermission(AttendancePermissions.TimesheetResolveKey)]
public sealed record SetWorkedHoursCommand(Guid AttendanceDayId, int WorkedMinutes, string? Reason) : ICommand<Guid>;

public sealed class SetWorkedHoursValidator : AbstractValidator<SetWorkedHoursCommand>
{
    public SetWorkedHoursValidator() => RuleFor(x => x.WorkedMinutes).GreaterThanOrEqualTo(0);
}

public sealed class SetWorkedHoursHandler(
    IAttendanceDayRepository days, IAnomalyResolutionRepository resolutions, IDataScopeService dataScope, ITenantContext tenant, ICurrentUser currentUser, TimeProvider clock)
    : ICommandHandler<SetWorkedHoursCommand, Guid>
{
    public async Task<Result<Guid>> Handle(SetWorkedHoursCommand command, CancellationToken cancellationToken)
    {
        var found = await days.GetByIdAsync(command.AttendanceDayId, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var day = found.Value;
        if (!await dataScope.CanAccessEmployeeAsync(AttendancePermissions.TimesheetResolveKey, day.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(AttendanceErrors.EmployeeNotInScope);
        }

        if (!AnomalyResolutionAdvisor.IsAllowed(day.Anomalies, AnomalyResolutionAction.SetWorkedHours))
        {
            return Error.Validation(AttendanceDomainErrors.AnomalyActionNotAllowed);
        }

        var anomaly = day.Anomalies;
        var resolved = day.ResolveWithOverride(command.WorkedMinutes, command.Reason);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        resolutions.Add(AnomalyResolution.Create(
            tenant.TenantId, day.Id, anomaly, AnomalyResolutionAction.SetWorkedHours, null, null, currentUser.UserId, clock.GetUtcNow().UtcDateTime));
        return day.Id;
    }
}

/// <summary>Dismisses a flagged day without an override or a linked Leave record.</summary>
[RequiresPermission(AttendancePermissions.TimesheetResolveKey)]
public sealed record IgnoreAttendanceAnomalyCommand(Guid AttendanceDayId) : ICommand<Guid>;

public sealed class IgnoreAttendanceAnomalyHandler(
    IAttendanceDayRepository days, IAnomalyResolutionRepository resolutions, IDataScopeService dataScope, ITenantContext tenant, ICurrentUser currentUser, TimeProvider clock)
    : ICommandHandler<IgnoreAttendanceAnomalyCommand, Guid>
{
    public async Task<Result<Guid>> Handle(IgnoreAttendanceAnomalyCommand command, CancellationToken cancellationToken)
    {
        var found = await days.GetByIdAsync(command.AttendanceDayId, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var day = found.Value;
        if (!await dataScope.CanAccessEmployeeAsync(AttendancePermissions.TimesheetResolveKey, day.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(AttendanceErrors.EmployeeNotInScope);
        }

        var anomaly = day.Anomalies;
        var ignored = day.Ignore();
        if (ignored.IsFailure)
        {
            return ignored.Error;
        }

        resolutions.Add(AnomalyResolution.Create(
            tenant.TenantId, day.Id, anomaly, AnomalyResolutionAction.Ignore, null, null, currentUser.UserId, clock.GetUtcNow().UtcDateTime));
        return day.Id;
    }
}

/// <summary>
/// "Fazla mesai ekle" resolution (11.3.6 Fazla çalışma/Geç çıkış): opens an OvertimeRequest in Leave for the day's
/// employee via ILeaveCommandService (Source=Admin, still runs Leave's own Workflow approval - 11.3.06 open point #3).
/// </summary>
[RequiresPermission(AttendancePermissions.TimesheetResolveKey)]
public sealed record AddOvertimeFromAnomalyCommand(Guid AttendanceDayId, TimeOnly StartTime, int DurationMinutes, string? Description) : ICommand<Guid>;

public sealed class AddOvertimeFromAnomalyValidator : AbstractValidator<AddOvertimeFromAnomalyCommand>
{
    public AddOvertimeFromAnomalyValidator() => RuleFor(x => x.DurationMinutes).GreaterThan(0);
}

public sealed class AddOvertimeFromAnomalyHandler(
    IAttendanceDayRepository days, IAnomalyResolutionRepository resolutions, IDataScopeService dataScope, ITenantContext tenant, ICurrentUser currentUser,
    ILeaveCommandService leave, TimeProvider clock)
    : ICommandHandler<AddOvertimeFromAnomalyCommand, Guid>
{
    public async Task<Result<Guid>> Handle(AddOvertimeFromAnomalyCommand command, CancellationToken cancellationToken)
    {
        var found = await days.GetByIdAsync(command.AttendanceDayId, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var day = found.Value;
        if (!await dataScope.CanAccessEmployeeAsync(AttendancePermissions.TimesheetResolveKey, day.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(AttendanceErrors.EmployeeNotInScope);
        }

        if (day.Anomalies == AttendanceAnomaly.None || day.ResolutionState != AttendanceResolutionState.Unresolved)
        {
            return Error.Validation(AttendanceDomainErrors.DayAlreadyResolved);
        }

        if (!AnomalyResolutionAdvisor.IsAllowed(day.Anomalies, AnomalyResolutionAction.AddOvertime))
        {
            return Error.Validation(AttendanceDomainErrors.AnomalyActionNotAllowed);
        }

        var overtime = await leave.CreateOvertimeRequestAsync(day.EmployeeId, day.Date, command.StartTime, command.DurationMinutes, command.Description, cancellationToken);
        if (overtime.IsFailure)
        {
            return overtime.Error;
        }

        var anomaly = day.Anomalies;
        var resolved = day.MarkResolvedByLinkedEntity();
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        resolutions.Add(AnomalyResolution.Create(
            tenant.TenantId, day.Id, anomaly, AnomalyResolutionAction.AddOvertime, LinkedEntityTypes.OvertimeRequest, overtime.Value, currentUser.UserId, clock.GetUtcNow().UtcDateTime));
        return day.Id;
    }
}

/// <summary>
/// "İzin talebi" resolution (11.3.6 Az çalışma, "mesai yok, çünkü az çalışma var"): a single-day leave request for the
/// flagged day, approved at once via ILeaveCommandService (11.3.6 "Yetkili puantajdan izin eklerse onay sürecine
/// girmeden Onaylanmış").
/// </summary>
[RequiresPermission(AttendancePermissions.TimesheetResolveKey)]
public sealed record CreateLeaveRequestFromAnomalyCommand(Guid AttendanceDayId, Guid LeaveTypeId, string? Description) : ICommand<Guid>;

public sealed class CreateLeaveRequestFromAnomalyHandler(
    IAttendanceDayRepository days, IAnomalyResolutionRepository resolutions, IDataScopeService dataScope, ITenantContext tenant, ICurrentUser currentUser,
    ILeaveCommandService leave, TimeProvider clock)
    : ICommandHandler<CreateLeaveRequestFromAnomalyCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateLeaveRequestFromAnomalyCommand command, CancellationToken cancellationToken)
    {
        var found = await days.GetByIdAsync(command.AttendanceDayId, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var day = found.Value;
        if (!await dataScope.CanAccessEmployeeAsync(AttendancePermissions.TimesheetResolveKey, day.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(AttendanceErrors.EmployeeNotInScope);
        }

        if (day.Anomalies == AttendanceAnomaly.None || day.ResolutionState != AttendanceResolutionState.Unresolved)
        {
            return Error.Validation(AttendanceDomainErrors.DayAlreadyResolved);
        }

        if (!AnomalyResolutionAdvisor.IsAllowed(day.Anomalies, AnomalyResolutionAction.CreateLeaveRequest))
        {
            return Error.Validation(AttendanceDomainErrors.AnomalyActionNotAllowed);
        }

        var leaveRequest = await leave.CreateApprovedLeaveAsync(day.EmployeeId, command.LeaveTypeId, day.Date, day.Date, command.Description, cancellationToken);
        if (leaveRequest.IsFailure)
        {
            return leaveRequest.Error;
        }

        var anomaly = day.Anomalies;
        var resolved = day.MarkResolvedByLinkedEntity();
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        resolutions.Add(AnomalyResolution.Create(
            tenant.TenantId, day.Id, anomaly, AnomalyResolutionAction.CreateLeaveRequest, LinkedEntityTypes.LeaveRequest, leaveRequest.Value, currentUser.UserId, clock.GetUtcNow().UtcDateTime));
        return day.Id;
    }
}

/// <summary>Audit trail for a day's cell drawer (11.3.6 "hücre çekmecesi: öneriler, hareketler").</summary>
[RequiresPermission("attendance.timesheet.view")]
public sealed record ListAnomalyResolutionsQuery(Guid AttendanceDayId) : IQuery<IReadOnlyList<AnomalyResolutionDto>>;

public sealed class ListAnomalyResolutionsHandler(IAnomalyResolutionRepository resolutions)
    : IQueryHandler<ListAnomalyResolutionsQuery, IReadOnlyList<AnomalyResolutionDto>>
{
    public async Task<Result<IReadOnlyList<AnomalyResolutionDto>>> Handle(ListAnomalyResolutionsQuery query, CancellationToken cancellationToken) =>
        (await resolutions.ListForDayAsync(query.AttendanceDayId, cancellationToken)).Select(AnomalyResolutionMapper.ToDto).ToList();
}

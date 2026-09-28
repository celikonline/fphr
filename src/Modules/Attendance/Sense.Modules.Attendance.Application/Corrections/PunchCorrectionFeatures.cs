using FluentValidation;
using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Corrections;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Workflow.Contracts;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.Corrections;

/// <summary>One missing punch correction request as the employee sees it ("Taleplerim").</summary>
public sealed record PunchCorrectionRequestDto(
    Guid Id, DateOnly Date, TimeOnly? RequestedCheckIn, TimeOnly? RequestedCheckOut, TimeOnly? CurrentCheckIn, TimeOnly? CurrentCheckOut,
    string Reason, PunchCorrectionStatus Status, Guid? ApprovalRequestId, DateTime SubmittedAt, DateTime? DecidedAt);

internal static class PunchCorrectionMapper
{
    public static PunchCorrectionRequestDto ToDto(PunchCorrectionRequest r) =>
        new(r.Id, r.AttendanceDate, r.RequestedCheckIn, r.RequestedCheckOut, r.CurrentCheckIn, r.CurrentCheckOut, r.Reason, r.Status,
            r.ApprovalRequestId, r.SubmittedAt, r.DecidedAt);
}

/// <summary>
/// Kilit kuralı (FG-004, plan Karar 10): a day's month is closed for corrections when its shift plan is locked
/// (ShiftPlanLock) or the employee's monthly timesheet has been approved (MonthlyTimesheetApproval). Checked both when the
/// request is submitted and again when Workflow approves it.
/// </summary>
internal static class PunchCorrectionPeriodLock
{
    public static async Task<bool> IsLockedAsync(
        IShiftPlanLockRepository locks, IMonthlyTimesheetApprovalRepository approvals, Guid employeeId, DateOnly date, CancellationToken cancellationToken) =>
        await locks.FindByPeriodAsync(date, cancellationToken) is { IsLocked: true }
        || await approvals.FindAsync(employeeId, date, cancellationToken) is not null;
}

/// <summary>"Düzeltme iste" (FG-004): the caller asks for a check-in and/or check-out time on one past or current day.</summary>
[RequiresPermission(AttendancePermissions.MyCorrectionsKey)]
public sealed record SubmitMyPunchCorrectionCommand(DateOnly Date, TimeOnly? CheckIn, TimeOnly? CheckOut, string? Reason) : ICommand<PunchCorrectionRequestDto>;

public sealed class SubmitMyPunchCorrectionValidator : AbstractValidator<SubmitMyPunchCorrectionCommand>
{
    public SubmitMyPunchCorrectionValidator() => RuleFor(x => x.Reason).MaximumLength(PunchCorrectionLimits.ReasonMaxLength);
}

public sealed class SubmitMyPunchCorrectionHandler(
    ITenantContext tenant, ICurrentUser currentUser, IPunchCorrectionRequestRepository corrections, IAttendanceDayRepository days,
    IShiftPlanLockRepository locks, IMonthlyTimesheetApprovalRepository approvals, IWorkflowService workflow, ITenantLocalClock localClock,
    TimeProvider clock)
    : ICommandHandler<SubmitMyPunchCorrectionCommand, PunchCorrectionRequestDto>
{
    public async Task<Result<PunchCorrectionRequestDto>> Handle(SubmitMyPunchCorrectionCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(AttendanceErrors.EmployeeNotFound);
        }

        var localToday = DateOnly.FromDateTime((await localClock.NowAsync(tenant.TenantId, cancellationToken)).Local);
        if (command.Date > localToday)
        {
            return Error.Validation(AttendanceErrors.CorrectionFutureDate);
        }

        if (await PunchCorrectionPeriodLock.IsLockedAsync(locks, approvals, employeeId, command.Date, cancellationToken))
        {
            return Error.Conflict(AttendanceErrors.CorrectionPeriodLocked);
        }

        if (await corrections.HasPendingAsync(employeeId, command.Date, cancellationToken))
        {
            return Error.Conflict(AttendanceErrors.CorrectionPendingExists);
        }

        var day = await days.FindByEmployeeAndDateAsync(employeeId, command.Date, cancellationToken);
        var created = PunchCorrectionRequest.Submit(
            tenant.TenantId, employeeId, command.Date, command.CheckIn, command.CheckOut, command.Reason, day?.CheckIn, day?.CheckOut,
            clock.GetUtcNow().UtcDateTime);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var approval = await workflow.StartAsync(
            ApprovalRequestTypes.PunchCorrection, created.Value.Id, employeeId, null, currentUser.IpAddress, null, cancellationToken);
        if (approval.IsFailure)
        {
            return approval.Error;
        }

        created.Value.AttachApproval(approval.Value);
        corrections.Add(created.Value);
        return PunchCorrectionMapper.ToDto(created.Value);
    }
}

/// <summary>Geri çek (FG-004): only the caller's own Pending request; someone else's id looks like an unknown one (404).</summary>
[RequiresPermission(AttendancePermissions.MyCorrectionsKey)]
public sealed record WithdrawMyPunchCorrectionCommand(Guid RequestId) : ICommand;

public sealed class WithdrawMyPunchCorrectionHandler(
    ICurrentUser currentUser, IPunchCorrectionRequestRepository corrections, IWorkflowService workflow, TimeProvider clock)
    : ICommandHandler<WithdrawMyPunchCorrectionCommand>
{
    public async Task<Result> Handle(WithdrawMyPunchCorrectionCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(AttendanceErrors.EmployeeNotFound);
        }

        var request = await corrections.FindByIdAsync(command.RequestId, cancellationToken);
        if (request is null || request.EmployeeId != employeeId)
        {
            return Error.NotFound(AttendanceDomainErrors.CorrectionNotFound);
        }

        var withdrawn = request.Withdraw(clock.GetUtcNow().UtcDateTime);
        if (withdrawn.IsFailure)
        {
            return withdrawn;
        }

        return request.ApprovalRequestId is { } approvalId ? await workflow.CancelAsync(approvalId, employeeId, cancellationToken) : Result.Success();
    }
}

/// <summary>"Taleplerim" (FG-004): the caller's correction requests, optionally limited to days in [From, To].</summary>
[RequiresPermission(AttendancePermissions.MyCorrectionsKey)]
public sealed record ListMyPunchCorrectionsQuery(DateOnly? From, DateOnly? To) : IQuery<IReadOnlyList<PunchCorrectionRequestDto>>;

public sealed class ListMyPunchCorrectionsHandler(ICurrentUser currentUser, IPunchCorrectionRequestRepository corrections)
    : IQueryHandler<ListMyPunchCorrectionsQuery, IReadOnlyList<PunchCorrectionRequestDto>>
{
    public async Task<Result<IReadOnlyList<PunchCorrectionRequestDto>>> Handle(ListMyPunchCorrectionsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(AttendanceErrors.EmployeeNotFound);
        }

        if (query.From is { } from && query.To is { } to && from > to)
        {
            return Error.Validation(AttendanceErrors.DateRangeInvalid);
        }

        var list = await corrections.ListForEmployeeAsync(employeeId, query.From, query.To, PagingDefaults.MaxPageSize, cancellationToken);
        return list.Select(PunchCorrectionMapper.ToDto).ToList();
    }
}

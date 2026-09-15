using FluentValidation;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Contracts.Events;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Overtime;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Workflow.Contracts;
using Sense.Modules.Workflow.Contracts.Events;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Legislation;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Leave.Application.Overtime;

public static class OvertimeErrors
{
    public const string Overlap = "leave.errors.overtime_overlap";
    public const string ConsentRevoked = "leave.errors.overtime_consent_revoked";
    public const string NotOwner = "leave.request.not_owner";
}

/// <summary>Non-blocking findings returned with a submitted overtime request (11.3.5 "leave.warnings.*").</summary>
public static class OvertimeWarnings
{
    public const string ConsentMissing = "leave.warnings.overtime_consent_missing";
    public const string NightConsentMissing = "leave.warnings.overtime_night_consent_missing";
    public const string OverlapsSchedule = "leave.warnings.overtime_overlaps_schedule";
    public const string AnnualLimitExceeded = "leave.warnings.overtime_annual_limit_exceeded";
}

public sealed record OvertimeRequestDto(
    Guid Id, Guid EmployeeId, DateOnly WorkDate, TimeOnly StartTime, int DurationMinutes, DateTime StartAt, DateTime EndAt, OvertimeDayKind DayKind,
    int OverlapWithScheduleMinutes, int PayableMinutes, string? Description, OvertimeRequestSource Source, OvertimeRequestStatus Status,
    Guid? ApprovalRequestId, DateTime SubmittedAt, DateTime? DecidedAt, Guid? ConversionId);

public sealed record OvertimeSubmissionDto(Guid Id, int OverlapWithScheduleMinutes, int PayableMinutes, IReadOnlyList<string> Warnings);

internal static class OvertimeMapper
{
    public static OvertimeRequestDto ToDto(OvertimeRequest r) =>
        new(r.Id, r.EmployeeId, r.WorkDate, r.StartTime, r.DurationMinutes, r.StartAt, r.EndAt, r.DayKind, r.OverlapWithScheduleMinutes, r.PayableMinutes,
            r.Description, r.Source, r.Status, r.ApprovalRequestId, r.SubmittedAt, r.DecidedAt, r.ConversionId);
}

public sealed record OvertimeEvaluation(OvertimeDayKind DayKind, int OverlapWithScheduleMinutes, IReadOnlyList<string> Warnings);

/// <summary>
/// Overtime submit checks: overlap with other active overtime (error), consent (revoked = error, missing / no night consent =
/// warning), planned-schedule overlap (PayableMinutes + warning) and the annual limit (Overtime.AnnualLimitHours, warning).
/// </summary>
public sealed class OvertimeRequestRules(
    IOvertimeRequestRepository requests,
    IOvertimeConsentRepository consents,
    IWorkScheduleReader schedules,
    IHolidayCalendarLookup holidays,
    ILegislationParameterReader legislation,
    IIntegrationEventOutbox outbox)
{
    public const decimal FallbackAnnualLimitHours = 270m;

    public async Task<Result<OvertimeEvaluation>> EvaluateAsync(Guid tenantId, Guid employeeId, DateOnly workDate, TimeOnly startTime, int durationMinutes, CancellationToken ct)
    {
        var warnings = new List<string>();
        var start = workDate.ToDateTime(startTime);
        var end = start.AddMinutes(durationMinutes);
        var lastDay = DateOnly.FromDateTime(end);

        var nearby = await requests.ListActiveAsync(employeeId, workDate.AddDays(-1), lastDay, ct);
        if (nearby.Any(r => r.Overlaps(start, end)))
        {
            return Error.Conflict(OvertimeErrors.Overlap);
        }

        var employeeConsents = await consents.ListByEmployeeAsync(employeeId, ct);
        switch (OvertimeMath.ConsentState(employeeConsents, workDate, out var consent))
        {
            case OvertimeConsentState.Revoked:
                return Error.Validation(OvertimeErrors.ConsentRevoked);
            case OvertimeConsentState.Missing:
                warnings.Add(OvertimeWarnings.ConsentMissing);
                break;
            default:
                if (consent is { CoversNightWork: false } && OvertimeMath.TouchesNight(start, durationMinutes))
                {
                    warnings.Add(OvertimeWarnings.NightConsentMissing);
                }

                break;
        }

        var holidayDates = new HashSet<DateOnly>();
        if (await holidays.ResolveCalendarAsync(tenantId, null, null, ct) is { } calendar)
        {
            holidayDates = (await holidays.GetHolidaysAsync(tenantId, calendar.Id, workDate, lastDay, ct)).Select(h => h.Date).ToHashSet();
        }

        var intervals = (await schedules.GetScheduledIntervalsAsync(tenantId, employeeId, workDate, lastDay, ct))
            .Where(i => !holidayDates.Contains(DateOnly.FromDateTime(i.Start)))
            .ToList();
        var overlap = OvertimeMath.OverlapMinutes(start, durationMinutes, intervals);
        if (overlap > 0)
        {
            warnings.Add(OvertimeWarnings.OverlapsSchedule);
        }

        var isWorkingDay = await schedules.GetWorkingDayPredicateAsync(tenantId, employeeId, workDate, workDate, ct);
        var dayKind = holidayDates.Contains(workDate) ? OvertimeDayKind.PublicHoliday
            : isWorkingDay(workDate) ? OvertimeDayKind.Workday
            : OvertimeDayKind.WeeklyRest;

        var limitHours = await legislation.GetDecimalOrDefaultAsync(LegislationParameterKeys.OvertimeAnnualLimitHours, workDate, FallbackAnnualLimitHours, ct);
        var yearStart = new DateOnly(workDate.Year, 1, 1);
        var yearTotal = (await requests.ListActiveAsync(employeeId, yearStart, yearStart.AddYears(1).AddDays(-1), ct)).Sum(r => r.DurationMinutes);
        if (yearTotal + durationMinutes > limitHours * 60m)
        {
            warnings.Add(OvertimeWarnings.AnnualLimitExceeded);
        }

        return new OvertimeEvaluation(dayKind, overlap, warnings);
    }

    /// <summary>Evaluate → create Pending → start the Workflow "Overtime" approval with the employee as requester.</summary>
    public async Task<Result<(OvertimeRequest Request, OvertimeSubmissionDto Submission)>> SubmitAsync(
        IWorkflowService workflow, Guid tenantId, Guid employeeId, DateOnly workDate, TimeOnly startTime, int durationMinutes, string? description,
        OvertimeRequestSource source, DateTime now, Guid? userId, string? ip, CancellationToken ct)
    {
        var evaluated = await EvaluateAsync(tenantId, employeeId, workDate, startTime, durationMinutes, ct);
        if (evaluated.IsFailure)
        {
            return evaluated.Error;
        }

        var e = evaluated.Value;
        var created = OvertimeRequest.Submit(
            tenantId, employeeId, workDate, startTime, durationMinutes, e.DayKind, e.OverlapWithScheduleMinutes, description, source, now, userId, ip);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var approval = await workflow.StartAsync(ApprovalRequestTypes.Overtime, created.Value.Id, employeeId, null, ip, null, ct);
        if (approval.IsFailure)
        {
            return approval.Error;
        }

        created.Value.AttachApproval(approval.Value);
        requests.Add(created.Value);
        var r = created.Value;
        outbox.Enqueue(new OvertimeRequestSubmitted(tenantId, r.Id, employeeId, r.WorkDate, r.StartTime, r.DurationMinutes, r.PayableMinutes, r.Source.ToString()));
        return (created.Value, new OvertimeSubmissionDto(created.Value.Id, created.Value.OverlapWithScheduleMinutes, created.Value.PayableMinutes, e.Warnings));
    }
}

/// <summary>Self-service overtime request ("Talep → Fazla mesai").</summary>
[RequiresPermission("leave.my.overtimes")]
public sealed record SubmitMyOvertimeRequestCommand(DateOnly WorkDate, TimeOnly StartTime, int DurationMinutes, string? Description) : ICommand<OvertimeSubmissionDto>;

public sealed class SubmitMyOvertimeRequestValidator : AbstractValidator<SubmitMyOvertimeRequestCommand>
{
    public SubmitMyOvertimeRequestValidator()
    {
        RuleFor(x => x.DurationMinutes).InclusiveBetween(1, OvertimeLimits.MaxDurationMinutes);
        RuleFor(x => x.Description).MaximumLength(LeaveLimits.DescriptionMaxLength);
    }
}

public sealed class SubmitMyOvertimeRequestHandler(
    ITenantContext tenant, ICurrentUser currentUser, OvertimeRequestRules rules, IWorkflowService workflow, TimeProvider clock)
    : ICommandHandler<SubmitMyOvertimeRequestCommand, OvertimeSubmissionDto>
{
    public async Task<Result<OvertimeSubmissionDto>> Handle(SubmitMyOvertimeRequestCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(LeaveErrors.NoEmployeeForUser);
        }

        var submitted = await rules.SubmitAsync(
            workflow, tenant.TenantId, employeeId, command.WorkDate, command.StartTime, command.DurationMinutes, command.Description,
            OvertimeRequestSource.Manual, clock.GetUtcNow().UtcDateTime, currentUser.UserId, currentUser.IpAddress, cancellationToken);
        return submitted.IsFailure ? submitted.Error : submitted.Value.Submission;
    }
}

/// <summary>CreateOvertimeRequestOnBehalf: a manager/HR adds overtime for an employee in scope (Source = Admin); approval still runs.</summary>
[RequiresPermission("leave.overtimes.create_on_behalf")]
public sealed record CreateOvertimeRequestOnBehalfCommand(Guid EmployeeId, DateOnly WorkDate, TimeOnly StartTime, int DurationMinutes, string? Description)
    : ICommand<OvertimeSubmissionDto>;

public sealed class CreateOvertimeRequestOnBehalfValidator : AbstractValidator<CreateOvertimeRequestOnBehalfCommand>
{
    public CreateOvertimeRequestOnBehalfValidator()
    {
        RuleFor(x => x.DurationMinutes).InclusiveBetween(1, OvertimeLimits.MaxDurationMinutes);
        RuleFor(x => x.Description).MaximumLength(LeaveLimits.DescriptionMaxLength);
    }
}

public sealed class CreateOvertimeRequestOnBehalfHandler(
    ITenantContext tenant, ICurrentUser currentUser, IDataScopeService dataScope, OvertimeRequestRules rules, IWorkflowService workflow, TimeProvider clock)
    : ICommandHandler<CreateOvertimeRequestOnBehalfCommand, OvertimeSubmissionDto>
{
    public async Task<Result<OvertimeSubmissionDto>> Handle(CreateOvertimeRequestOnBehalfCommand command, CancellationToken cancellationToken)
    {
        if (!await dataScope.CanAccessEmployeeAsync(LeavePermissions.OvertimesCreateOnBehalf.Key, command.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(LeaveErrors.EmployeeNotInScope);
        }

        var submitted = await rules.SubmitAsync(
            workflow, tenant.TenantId, command.EmployeeId, command.WorkDate, command.StartTime, command.DurationMinutes, command.Description,
            OvertimeRequestSource.Admin, clock.GetUtcNow().UtcDateTime, currentUser.UserId, currentUser.IpAddress, cancellationToken);
        return submitted.IsFailure ? submitted.Error : submitted.Value.Submission;
    }
}

[RequiresPermission("leave.my.overtimes")]
public sealed record WithdrawOvertimeRequestCommand(Guid RequestId) : ICommand;

public sealed class WithdrawOvertimeRequestHandler(ICurrentUser currentUser, IOvertimeRequestRepository requests, IWorkflowService workflow, TimeProvider clock)
    : ICommandHandler<WithdrawOvertimeRequestCommand>
{
    public async Task<Result> Handle(WithdrawOvertimeRequestCommand command, CancellationToken cancellationToken)
    {
        var request = await requests.FindByIdAsync(command.RequestId, cancellationToken);
        if (request is null)
        {
            return Error.NotFound(LeaveDomainErrors.OvertimeNotFound);
        }

        if (currentUser.EmployeeId != request.EmployeeId)
        {
            return Error.Forbidden(OvertimeErrors.NotOwner);
        }

        var withdrawn = request.Withdraw(clock.GetUtcNow().UtcDateTime);
        if (withdrawn.IsFailure)
        {
            return withdrawn;
        }

        return request.ApprovalRequestId is { } approvalId ? await workflow.CancelAsync(approvalId, request.EmployeeId, cancellationToken) : Result.Success();
    }
}

[RequiresPermission("leave.my.overtimes")]
public sealed record ListMyOvertimeRequestsQuery : IQuery<IReadOnlyList<OvertimeRequestDto>>;

public sealed class ListMyOvertimeRequestsHandler(ICurrentUser currentUser, IOvertimeRequestRepository requests)
    : IQueryHandler<ListMyOvertimeRequestsQuery, IReadOnlyList<OvertimeRequestDto>>
{
    public async Task<Result<IReadOnlyList<OvertimeRequestDto>>> Handle(ListMyOvertimeRequestsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(LeaveErrors.NoEmployeeForUser);
        }

        IReadOnlyList<OvertimeRequestDto> list = (await requests.ListByEmployeeAsync(employeeId, cancellationToken)).Select(OvertimeMapper.ToDto).ToList();
        return Result.Success(list);
    }
}

[RequiresPermission("leave.overtimes.read")]
public sealed record ListEmployeeOvertimeRequestsQuery(Guid EmployeeId) : IQuery<IReadOnlyList<OvertimeRequestDto>>;

public sealed class ListEmployeeOvertimeRequestsHandler(IOvertimeRequestRepository requests, IDataScopeService dataScope)
    : IQueryHandler<ListEmployeeOvertimeRequestsQuery, IReadOnlyList<OvertimeRequestDto>>
{
    public async Task<Result<IReadOnlyList<OvertimeRequestDto>>> Handle(ListEmployeeOvertimeRequestsQuery query, CancellationToken cancellationToken)
    {
        if (!await dataScope.CanAccessEmployeeAsync(LeavePermissions.OvertimesRead.Key, query.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(LeaveErrors.EmployeeNotInScope);
        }

        IReadOnlyList<OvertimeRequestDto> list = (await requests.ListByEmployeeAsync(query.EmployeeId, cancellationToken)).Select(OvertimeMapper.ToDto).ToList();
        return Result.Success(list);
    }
}

/// <summary>Workflow approved an "Overtime" request → Approved.</summary>
public sealed class OvertimeApprovalCompletedHandler(IOvertimeRequestRepository requests, IIntegrationEventOutbox outbox, ILeaveUnitOfWork unitOfWork, TimeProvider clock)
    : IIntegrationEventHandler<ApprovalCompleted>
{
    public async Task Handle(ApprovalCompleted integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.RequestType != ApprovalRequestTypes.Overtime
            || await requests.FindByIdAsync(integrationEvent.SubjectId, cancellationToken) is not { } request
            || request.Approve(integrationEvent.ApprovalRequestId, clock.GetUtcNow().UtcDateTime).IsFailure)
        {
            return;
        }

        outbox.Enqueue(new OvertimeApproved(request.TenantId, request.Id, request.EmployeeId, request.WorkDate, request.DurationMinutes, request.PayableMinutes));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Workflow rejected an "Overtime" request → Rejected.</summary>
public sealed class OvertimeApprovalRejectedHandler(IOvertimeRequestRepository requests, IIntegrationEventOutbox outbox, ILeaveUnitOfWork unitOfWork, TimeProvider clock)
    : IIntegrationEventHandler<ApprovalRejected>
{
    public async Task Handle(ApprovalRejected integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.RequestType != ApprovalRequestTypes.Overtime
            || await requests.FindByIdAsync(integrationEvent.SubjectId, cancellationToken) is not { } request
            || request.Reject(integrationEvent.ApprovalRequestId, clock.GetUtcNow().UtcDateTime).IsFailure)
        {
            return;
        }

        outbox.Enqueue(new OvertimeRejected(request.TenantId, request.Id, request.EmployeeId));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

using FluentValidation;
using Sense.Modules.Leave.Application.Balances;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Balances;
using Sense.Modules.Leave.Domain.Calendar;
using Sense.Modules.Leave.Domain.Ledger;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Workflow.Contracts;
using Sense.Modules.Workflow.Contracts.Events;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Leave.Application.Requests;

public static class LeaveRequestErrors
{
    public const string TypeNotAvailable = "leave.errors.type_not_available";
    public const string Overlap = "leave.errors.overlap";
    public const string AbovePerRequestLimit = "leave.errors.above_per_request_limit";
    public const string BelowMinDays = "leave.errors.below_min_days";
    public const string AboveMaxDays = "leave.errors.above_max_days";
    public const string InsufficientBalance = "leave.errors.insufficient_balance";
    public const string DescriptionRequired = "leave.errors.description_required";
    public const string NotOwner = "leave.request.not_owner";
    public const string BypassNotAllowed = "leave.errors.bypass_approval_not_allowed";
}

public sealed record LeaveRequestDto(
    Guid Id, Guid EmployeeId, Guid LeaveTypeId, LeaveTypeCode LeaveTypeCode, DateOnly StartDate, DateOnly EndDate, decimal CalculatedDays,
    string? Description, LeaveRequestStatus Status, LeaveRequestSource Source, Guid? ApprovalRequestId, int RevisionNo, DateTime SubmittedAt, DateTime? DecidedAt,
    string? LeaveTypeName = null, Guid? PreviousRequestId = null, DateTime? CancelledAt = null, string? CancellationReason = null,
    TimeOnly? StartTime = null, TimeOnly? EndTime = null, HalfDayPeriod? HalfDayPeriod = null);

internal static class LeaveRequestMapper
{
    public static LeaveRequestDto ToDto(LeaveRequest r, string? typeName = null) =>
        new(r.Id, r.EmployeeId, r.LeaveTypeId, r.LeaveTypeCodeSnapshot, r.StartDate, r.EndDate, r.CalculatedDays, r.Description, r.Status, r.Source,
            r.ApprovalRequestId, r.RevisionNo, r.SubmittedAt, r.DecidedAt, typeName, r.PreviousRequestId, r.CancelledAt, r.CancellationReason,
            r.StartTime, r.EndTime, r.HalfDayPeriod);

    /// <summary>
    /// Maps requests with the leave type name (stored name; seeded types carry a resource key the API localizes) from the employee's
    /// rule set, falling back to the tenant's other sets for requests of a type the employee no longer has (rule set reassigned).
    /// </summary>
    public static async Task<IReadOnlyList<LeaveRequestDto>> ToDtosAsync(
        Guid employeeId, IReadOnlyList<LeaveRequest> requests, IEmployeeLeaveProfileRepository profiles, ILeaveRuleSetRepository ruleSets, CancellationToken ct)
    {
        var names = new Dictionary<Guid, string>();
        if (requests.Count > 0 && await profiles.FindByEmployeeIdAsync(employeeId, ct) is { } profile && await ruleSets.GetByIdAsync(profile.RuleSetId, ct) is { IsSuccess: true } set)
        {
            foreach (var type in set.Value.Types)
            {
                names[type.Id] = type.Name;
            }
        }

        if (requests.Any(r => !names.ContainsKey(r.LeaveTypeId)))
        {
            foreach (var type in (await ruleSets.ListAsync(ct)).SelectMany(s => s.Types))
            {
                names.TryAdd(type.Id, type.Name);
            }
        }

        return requests.Select(r => ToDto(r, names.GetValueOrDefault(r.LeaveTypeId))).ToList();
    }
}

/// <summary>Validation shared by submit and edit (11.3.5 "Talep doğrulama" steps 2, 4, 6, 7, 9, 11, 12).</summary>
public sealed class LeaveRequestRules(
    IEmployeeLeaveProfileRepository profiles,
    ILeaveRuleSetRepository ruleSets,
    ILeaveRequestRepository requests,
    ILeaveLedgerRepository ledger,
    ILeaveDayCalculator dayCalculator,
    LeaveBalanceService balances)
{
    public async Task<Result<(LeaveType Type, decimal Days)>> ValidateAsync(
        Guid tenantId, Guid employeeId, Guid leaveTypeId, DateOnly start, DateOnly end, string? description, Guid? editedRequestId, CancellationToken ct,
        bool requireSelfService = true, TimeOnly? startTime = null, TimeOnly? endTime = null, HalfDayPeriod? halfDayPeriod = null)
    {
        if (end < start)
        {
            return Error.Validation(LeaveDomainErrors.RequestInvalidRange);
        }

        var profile = await profiles.FindByEmployeeIdAsync(employeeId, ct);
        if (profile is null)
        {
            return Error.NotFound(LeaveErrors.ProfileNotFound);
        }

        var set = await ruleSets.GetByIdAsync(profile.RuleSetId, ct);
        if (set.IsFailure)
        {
            return set.Error;
        }

        var type = set.Value.Types.FirstOrDefault(t => t.Id == leaveTypeId);
        if (type is null || type.IsDisabled || (requireSelfService && !type.AllowSelfServiceRequest))
        {
            return Error.Validation(LeaveRequestErrors.TypeNotAvailable);
        }

        if (type.RequiresDescription && string.IsNullOrWhiteSpace(description))
        {
            return Error.Validation(LeaveRequestErrors.DescriptionRequired);
        }

        // The type's configured Unit dictates what the request must carry: Hourly needs a time range, HalfDay a
        // period flag, both single-day; a FullDay type rejects either (it has no matching UI to produce them).
        var partial = ValidatePartialDayShape(type.Unit, start, end, startTime, endTime, halfDayPeriod);
        if (partial.IsFailure)
        {
            return partial.Error;
        }

        decimal days;
        if (type.Unit == LeaveUnit.FullDay)
        {
            days = (await dayCalculator.CalculateAsync(tenantId, employeeId, start, end, type.CountsNonWorkingDays, ct)).Days;
        }
        else
        {
            // Hourly/HalfDay still need the single day to be open (not a holiday/weekly rest the type doesn't count).
            var dayOpen = (await dayCalculator.CalculateAsync(tenantId, employeeId, start, end, type.CountsNonWorkingDays, ct)).Days > 0m;
            days = !dayOpen ? 0m : type.Unit == LeaveUnit.Hourly ? LeavePartialDay.HourlyFraction(startTime!.Value, endTime!.Value) : LeavePartialDay.HalfDayFraction;
        }

        if (days <= 0m)
        {
            return Error.Validation(LeaveDomainErrors.RequestZeroDays);
        }

        // ListActiveOverlappingAsync only narrows by date range (SQL-friendly); the actual conflict (same-day
        // partial requests only clash if their time ranges intersect) is decided in memory by ConflictsWith.
        var overlapping = await requests.ListActiveOverlappingAsync(employeeId, start, end, ct);
        if (overlapping.Any(r => r.Id != editedRequestId && r.ConflictsWith(start, end, startTime, endTime, halfDayPeriod)))
        {
            return Error.Conflict(LeaveRequestErrors.Overlap);
        }

        if (type.LimitType == LeaveLimitType.PerRequest && days > type.PerRequestMaxDays)
        {
            return Error.Validation(LeaveRequestErrors.AbovePerRequestLimit, ("max", type.PerRequestMaxDays));
        }

        if (type.MinConsecutiveDays is { } min && days < min)
        {
            return Error.Validation(LeaveRequestErrors.BelowMinDays, ("min", min));
        }

        if (type.MaxConsecutiveDays is { } max && days > max)
        {
            return Error.Validation(LeaveRequestErrors.AboveMaxDays, ("max", max));
        }

        if (type.HasBalance && type.NegativeBalance != NegativeBalanceMode.Unlimited)
        {
            var entries = await ledger.ListAsync(employeeId, type.Id, ct);
            var projected = LeaveBalanceEngine.Project(type, profile.LeaveAccrualStartDate, balances.Today, entries);
            var pending = await requests.SumPendingDaysAsync(employeeId, type.Id, ct);
            if (editedRequestId is { } editedId && await requests.FindByIdAsync(editedId, ct) is { Status: LeaveRequestStatus.Pending } edited && edited.LeaveTypeId == type.Id)
            {
                pending -= edited.CalculatedDays;
            }

            var floor = type.NegativeBalance == NegativeBalanceMode.Limited ? -(type.NegativeBalanceLimitDays ?? 0m) : 0m;
            if (projected.AvailableBalance - pending - days < floor)
            {
                return Error.Validation(LeaveRequestErrors.InsufficientBalance, ("available", projected.AvailableBalance - pending));
            }
        }

        return (type, days);
    }

    /// <summary>Hourly needs a time range, HalfDay a period, both single-day; FullDay accepts neither.</summary>
    private static Result ValidatePartialDayShape(LeaveUnit unit, DateOnly start, DateOnly end, TimeOnly? startTime, TimeOnly? endTime, HalfDayPeriod? halfDayPeriod)
    {
        switch (unit)
        {
            case LeaveUnit.Hourly when startTime is null || endTime is null || endTime <= startTime:
            case LeaveUnit.HalfDay when halfDayPeriod is null:
                return Error.Validation(LeaveDomainErrors.RequestPartialDayTimeInvalid);
            case LeaveUnit.FullDay when startTime is not null || endTime is not null || halfDayPeriod is not null:
                return Error.Validation(LeaveDomainErrors.RequestPartialDayTimeInvalid);
            case LeaveUnit.Hourly or LeaveUnit.HalfDay when start != end:
                return Error.Validation(LeaveDomainErrors.RequestPartialDayRequiresSingleDate);
            default:
                return Result.Success();
        }
    }
}

/// <summary>
/// Self-service leave request: validate, persist as Pending and start the Workflow "Leave" approval. With
/// <see cref="PreviousRequestId"/> it re-submits the caller's own rejected request (Source = Resubmission); the rejected record stays.
/// </summary>
[RequiresPermission("leave.my.requests")]
public sealed record SubmitLeaveRequestCommand(
    Guid LeaveTypeId, DateOnly StartDate, DateOnly EndDate, string? Description, Guid? PreviousRequestId = null,
    TimeOnly? StartTime = null, TimeOnly? EndTime = null, HalfDayPeriod? HalfDayPeriod = null)
    : ICommand<LeaveRequestDto>;

public sealed class SubmitLeaveRequestValidator : AbstractValidator<SubmitLeaveRequestCommand>
{
    public SubmitLeaveRequestValidator() => RuleFor(x => x.Description).MaximumLength(LeaveLimits.DescriptionMaxLength);
}

public sealed class SubmitLeaveRequestHandler(
    ITenantContext tenant, ICurrentUser currentUser, LeaveRequestRules rules, ILeaveRequestRepository requests, IWorkflowService workflow, TimeProvider clock)
    : ICommandHandler<SubmitLeaveRequestCommand, LeaveRequestDto>
{
    public async Task<Result<LeaveRequestDto>> Handle(SubmitLeaveRequestCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(LeaveErrors.NoEmployeeForUser);
        }

        if (command.PreviousRequestId is { } previousId)
        {
            var previous = await requests.FindByIdAsync(previousId, cancellationToken);
            if (previous is null || previous.EmployeeId != employeeId || previous.Status != LeaveRequestStatus.Rejected)
            {
                return Error.Conflict(LeaveErrors.ResubmissionSourceInvalid);
            }
        }

        var valid = await rules.ValidateAsync(
            tenant.TenantId, employeeId, command.LeaveTypeId, command.StartDate, command.EndDate, command.Description, null, cancellationToken,
            startTime: command.StartTime, endTime: command.EndTime, halfDayPeriod: command.HalfDayPeriod);
        if (valid.IsFailure)
        {
            return valid.Error;
        }

        var submitted = LeaveRequest.Submit(
            tenant.TenantId, employeeId, valid.Value.Type.Id, valid.Value.Type.Code, command.StartDate, command.EndDate, valid.Value.Days,
            command.Description, command.PreviousRequestId is null ? LeaveRequestSource.SelfService : LeaveRequestSource.Resubmission,
            clock.GetUtcNow().UtcDateTime, currentUser.IpAddress, null, command.PreviousRequestId, command.StartTime, command.EndTime, command.HalfDayPeriod);
        if (submitted.IsFailure)
        {
            return submitted.Error;
        }

        var approval = await workflow.StartAsync(ApprovalRequestTypes.Leave, submitted.Value.Id, employeeId, null, currentUser.IpAddress, null, cancellationToken);
        if (approval.IsFailure)
        {
            return approval.Error;
        }

        submitted.Value.AttachApproval(approval.Value);
        requests.Add(submitted.Value);
        return LeaveRequestMapper.ToDto(submitted.Value, valid.Value.Type.Name);
    }
}

/// <summary>UpdatePendingLeaveRequest: requester edits a pending request; the old approval is cancelled and a new one started (RevisionNo++).</summary>
[RequiresPermission("leave.my.requests")]
public sealed record UpdatePendingLeaveRequestCommand(
    Guid RequestId, Guid LeaveTypeId, DateOnly StartDate, DateOnly EndDate, string? Description,
    TimeOnly? StartTime = null, TimeOnly? EndTime = null, HalfDayPeriod? HalfDayPeriod = null) : ICommand;

public sealed class UpdatePendingLeaveRequestHandler(
    ITenantContext tenant, ICurrentUser currentUser, LeaveRequestRules rules, ILeaveRequestRepository requests, IWorkflowService workflow)
    : ICommandHandler<UpdatePendingLeaveRequestCommand>
{
    public async Task<Result> Handle(UpdatePendingLeaveRequestCommand command, CancellationToken cancellationToken)
    {
        var request = await requests.FindByIdAsync(command.RequestId, cancellationToken);
        if (request is null)
        {
            return Error.NotFound(LeaveDomainErrors.RequestNotFound);
        }

        if (currentUser.EmployeeId != request.EmployeeId)
        {
            return Error.Forbidden(LeaveRequestErrors.NotOwner);
        }

        if (request.Status != LeaveRequestStatus.Pending)
        {
            return Error.Conflict(LeaveDomainErrors.RequestNotPending);
        }

        var valid = await rules.ValidateAsync(
            tenant.TenantId, request.EmployeeId, command.LeaveTypeId, command.StartDate, command.EndDate, command.Description, request.Id, cancellationToken,
            startTime: command.StartTime, endTime: command.EndTime, halfDayPeriod: command.HalfDayPeriod);
        if (valid.IsFailure)
        {
            return valid.Error;
        }

        if (request.ApprovalRequestId is { } previousApproval)
        {
            var cancelled = await workflow.CancelAsync(previousApproval, request.EmployeeId, cancellationToken);
            if (cancelled.IsFailure)
            {
                return cancelled;
            }
        }

        var updated = request.UpdatePending(
            valid.Value.Type.Id, valid.Value.Type.Code, command.StartDate, command.EndDate, valid.Value.Days, command.Description,
            command.StartTime, command.EndTime, command.HalfDayPeriod);
        if (updated.IsFailure)
        {
            return updated;
        }

        var approval = await workflow.StartAsync(ApprovalRequestTypes.Leave, request.Id, request.EmployeeId, null, currentUser.IpAddress, null, cancellationToken);
        if (approval.IsFailure)
        {
            return approval.Error;
        }

        request.AttachApproval(approval.Value);
        return Result.Success();
    }
}

/// <summary>Requester withdraws a pending request (Workflow approval cancelled).</summary>
[RequiresPermission("leave.my.requests")]
public sealed record WithdrawLeaveRequestCommand(Guid RequestId) : ICommand;

public sealed class WithdrawLeaveRequestHandler(ICurrentUser currentUser, ILeaveRequestRepository requests, IWorkflowService workflow, TimeProvider clock)
    : ICommandHandler<WithdrawLeaveRequestCommand>
{
    public async Task<Result> Handle(WithdrawLeaveRequestCommand command, CancellationToken cancellationToken)
    {
        var request = await requests.FindByIdAsync(command.RequestId, cancellationToken);
        if (request is null)
        {
            return Error.NotFound(LeaveDomainErrors.RequestNotFound);
        }

        if (currentUser.EmployeeId != request.EmployeeId)
        {
            return Error.Forbidden(LeaveRequestErrors.NotOwner);
        }

        var withdrawn = request.Withdraw(clock.GetUtcNow().UtcDateTime);
        if (withdrawn.IsFailure)
        {
            return withdrawn;
        }

        return request.ApprovalRequestId is { } approvalId ? await workflow.CancelAsync(approvalId, request.EmployeeId, cancellationToken) : Result.Success();
    }
}

[RequiresPermission("leave.my.requests")]
public sealed record ListMyLeaveRequestsQuery : IQuery<IReadOnlyList<LeaveRequestDto>>;

public sealed class ListMyLeaveRequestsHandler(
    ICurrentUser currentUser, ILeaveRequestRepository requests, IEmployeeLeaveProfileRepository profiles, ILeaveRuleSetRepository ruleSets)
    : IQueryHandler<ListMyLeaveRequestsQuery, IReadOnlyList<LeaveRequestDto>>
{
    public async Task<Result<IReadOnlyList<LeaveRequestDto>>> Handle(ListMyLeaveRequestsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(LeaveErrors.NoEmployeeForUser);
        }

        var list = await LeaveRequestMapper.ToDtosAsync(employeeId, await requests.ListByEmployeeAsync(employeeId, cancellationToken), profiles, ruleSets, cancellationToken);
        return Result.Success(list);
    }
}

[RequiresPermission("leave.requests.read")]
public sealed record ListEmployeeLeaveRequestsQuery(Guid EmployeeId) : IQuery<IReadOnlyList<LeaveRequestDto>>;

public sealed class ListEmployeeLeaveRequestsHandler(
    ILeaveRequestRepository requests, IDataScopeService dataScope, IEmployeeLeaveProfileRepository profiles, ILeaveRuleSetRepository ruleSets)
    : IQueryHandler<ListEmployeeLeaveRequestsQuery, IReadOnlyList<LeaveRequestDto>>
{
    public async Task<Result<IReadOnlyList<LeaveRequestDto>>> Handle(ListEmployeeLeaveRequestsQuery query, CancellationToken cancellationToken)
    {
        if (!await dataScope.CanAccessEmployeeAsync(LeavePermissions.RequestsRead.Key, query.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(LeaveErrors.EmployeeNotInScope);
        }

        var list = await LeaveRequestMapper.ToDtosAsync(
            query.EmployeeId, await requests.ListByEmployeeAsync(query.EmployeeId, cancellationToken), profiles, ruleSets, cancellationToken);
        return Result.Success(list);
    }
}

public sealed record RequestableLeaveTypeDto(
    Guid Id, LeaveTypeCode Code, string Name, string? Description, LeaveUnit Unit, bool IsPaid, bool HasBalance, decimal? PerRequestMaxDays,
    bool RequiresDescription, bool RequiresDocument, bool CountsNonWorkingDays);

/// <summary>"+ Talep": active, self-service types of the caller's rule set (name as stored; the API localizes resource keys).</summary>
[RequiresPermission("leave.my.requests")]
public sealed record ListMyRequestableLeaveTypesQuery : IQuery<IReadOnlyList<RequestableLeaveTypeDto>>;

public sealed class ListMyRequestableLeaveTypesHandler(
    ICurrentUser currentUser, IEmployeeLeaveProfileRepository profiles, ILeaveRuleSetRepository ruleSets, IDataScopeService dataScope)
    : IQueryHandler<ListMyRequestableLeaveTypesQuery, IReadOnlyList<RequestableLeaveTypeDto>>
{
    public async Task<Result<IReadOnlyList<RequestableLeaveTypeDto>>> Handle(ListMyRequestableLeaveTypesQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(LeaveErrors.NoEmployeeForUser);
        }

        var loaded = await new EmployeeLeaveContext(profiles, ruleSets, dataScope).LoadAsync(employeeId, null, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        IReadOnlyList<RequestableLeaveTypeDto> types = loaded.Value.RuleSet.Types
            .Where(t => !t.IsDisabled && t.AllowSelfServiceRequest)
            .OrderBy(t => t.SortOrder)
            .Select(t => new RequestableLeaveTypeDto(
                t.Id, t.Code, t.Name, t.Description, t.Unit, t.IsPaid, t.HasBalance, t.LimitType == LeaveLimitType.PerRequest ? t.PerRequestMaxDays : null,
                t.RequiresDescription, t.RequiresDocument, t.CountsNonWorkingDays))
            .ToList();
        return Result.Success(types);
    }
}

/// <summary>Live day count of a leave request draft; rule violations come back in <c>Warnings</c> instead of failing.</summary>
public sealed record LeaveRequestPreviewDto(decimal Days, int WeeklyRestDays, int PublicHolidayDays, IReadOnlyList<Error> Warnings);

internal static class LeaveRequestPreview
{
    public static async Task<Result<LeaveRequestPreviewDto>> BuildAsync(
        LeaveRequestRules rules, IEmployeeLeaveProfileRepository profiles, ILeaveRuleSetRepository ruleSets, ILeaveDayCalculator dayCalculator,
        Guid tenantId, Guid employeeId, Guid leaveTypeId, DateOnly start, DateOnly end, Guid? editedRequestId, bool requireSelfService, CancellationToken ct,
        TimeOnly? startTime = null, TimeOnly? endTime = null, HalfDayPeriod? halfDayPeriod = null)
    {
        if (end < start)
        {
            return Error.Validation(LeaveDomainErrors.RequestInvalidRange);
        }

        var profile = await profiles.FindByEmployeeIdAsync(employeeId, ct);
        if (profile is null)
        {
            return Error.NotFound(LeaveErrors.ProfileNotFound);
        }

        var set = await ruleSets.GetByIdAsync(profile.RuleSetId, ct);
        if (set.IsFailure)
        {
            return set.Error;
        }

        var type = set.Value.Types.FirstOrDefault(t => t.Id == leaveTypeId);
        if (type is null || type.IsDisabled || (requireSelfService && !type.AllowSelfServiceRequest))
        {
            return Error.Validation(LeaveRequestErrors.TypeNotAvailable);
        }

        var count = await dayCalculator.CalculateAsync(tenantId, employeeId, start, end, type.CountsNonWorkingDays, ct);
        // Description is not part of the preview; pass a placeholder so "description required" does not show up as a warning.
        var valid = await rules.ValidateAsync(
            tenantId, employeeId, leaveTypeId, start, end, "-", editedRequestId, ct, requireSelfService, startTime, endTime, halfDayPeriod);
        IReadOnlyList<Error> warnings = valid.IsFailure ? [valid.Error] : [];

        // On failure (e.g. missing time range while typing) show the best-effort fraction instead of the whole-day count.
        var days = valid.IsSuccess ? valid.Value.Days : type.Unit switch
        {
            LeaveUnit.Hourly when startTime is { } st && endTime is { } et && et > st => LeavePartialDay.HourlyFraction(st, et),
            LeaveUnit.HalfDay => LeavePartialDay.HalfDayFraction,
            _ => count.Days,
        };
        return new LeaveRequestPreviewDto(days, count.WeeklyRestDays, count.PublicHolidayDays, warnings);
    }
}

[RequiresPermission("leave.my.requests")]
public sealed record PreviewMyLeaveRequestQuery(
    Guid LeaveTypeId, DateOnly StartDate, DateOnly EndDate, Guid? EditedRequestId,
    TimeOnly? StartTime = null, TimeOnly? EndTime = null, HalfDayPeriod? HalfDayPeriod = null) : IQuery<LeaveRequestPreviewDto>;

public sealed class PreviewMyLeaveRequestHandler(
    ITenantContext tenant, ICurrentUser currentUser, LeaveRequestRules rules, IEmployeeLeaveProfileRepository profiles, ILeaveRuleSetRepository ruleSets,
    ILeaveDayCalculator dayCalculator)
    : IQueryHandler<PreviewMyLeaveRequestQuery, LeaveRequestPreviewDto>
{
    public async Task<Result<LeaveRequestPreviewDto>> Handle(PreviewMyLeaveRequestQuery query, CancellationToken cancellationToken) =>
        currentUser.EmployeeId is not { } employeeId
            ? Error.NotFound(LeaveErrors.NoEmployeeForUser)
            : await LeaveRequestPreview.BuildAsync(
                rules, profiles, ruleSets, dayCalculator, tenant.TenantId, employeeId, query.LeaveTypeId, query.StartDate, query.EndDate, query.EditedRequestId, true,
                cancellationToken, query.StartTime, query.EndTime, query.HalfDayPeriod);
}

[RequiresPermission("leave.requests.create_on_behalf")]
public sealed record PreviewEmployeeLeaveRequestQuery(
    Guid EmployeeId, Guid LeaveTypeId, DateOnly StartDate, DateOnly EndDate,
    TimeOnly? StartTime = null, TimeOnly? EndTime = null, HalfDayPeriod? HalfDayPeriod = null) : IQuery<LeaveRequestPreviewDto>;

public sealed class PreviewEmployeeLeaveRequestHandler(
    ITenantContext tenant, IDataScopeService dataScope, LeaveRequestRules rules, IEmployeeLeaveProfileRepository profiles, ILeaveRuleSetRepository ruleSets,
    ILeaveDayCalculator dayCalculator)
    : IQueryHandler<PreviewEmployeeLeaveRequestQuery, LeaveRequestPreviewDto>
{
    public async Task<Result<LeaveRequestPreviewDto>> Handle(PreviewEmployeeLeaveRequestQuery query, CancellationToken cancellationToken) =>
        !await dataScope.CanAccessEmployeeAsync(LeavePermissions.RequestsCreateOnBehalf.Key, query.EmployeeId, cancellationToken)
            ? Error.Forbidden(LeaveErrors.EmployeeNotInScope)
            : await LeaveRequestPreview.BuildAsync(
                rules, profiles, ruleSets, dayCalculator, tenant.TenantId, query.EmployeeId, query.LeaveTypeId, query.StartDate, query.EndDate, null, false,
                cancellationToken, query.StartTime, query.EndTime, query.HalfDayPeriod);
}

/// <summary>
/// İşlemler → İzin Ekle: a manager/HR adds leave for an employee in scope (Source = OnBehalf). With <c>BypassApproval</c>
/// ("Onay sürecine dahil edilmeden onaylansın", needs leave.requests.bypass_approval) it is Approved at once and the Usage row
/// is posted; otherwise the Workflow "Leave" approval starts as for self-service.
/// </summary>
[RequiresPermission("leave.requests.create_on_behalf")]
public sealed record CreateLeaveRequestOnBehalfCommand(
    Guid EmployeeId, Guid LeaveTypeId, DateOnly StartDate, DateOnly EndDate, string? Description, bool BypassApproval,
    TimeOnly? StartTime = null, TimeOnly? EndTime = null, HalfDayPeriod? HalfDayPeriod = null)
    : ICommand<LeaveRequestDto>;

public sealed class CreateLeaveRequestOnBehalfValidator : AbstractValidator<CreateLeaveRequestOnBehalfCommand>
{
    public CreateLeaveRequestOnBehalfValidator() => RuleFor(x => x.Description).MaximumLength(LeaveLimits.DescriptionMaxLength);
}

public sealed class CreateLeaveRequestOnBehalfHandler(
    ITenantContext tenant, ICurrentUser currentUser, IDataScopeService dataScope, LeaveRequestRules rules, ILeaveRequestRepository requests,
    ILeaveLedgerRepository ledger, IWorkflowService workflow, TimeProvider clock)
    : ICommandHandler<CreateLeaveRequestOnBehalfCommand, LeaveRequestDto>
{
    public async Task<Result<LeaveRequestDto>> Handle(CreateLeaveRequestOnBehalfCommand command, CancellationToken cancellationToken)
    {
        if (!await dataScope.CanAccessEmployeeAsync(LeavePermissions.RequestsCreateOnBehalf.Key, command.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(LeaveErrors.EmployeeNotInScope);
        }

        if (command.BypassApproval && !await dataScope.CanAccessEmployeeAsync(LeavePermissions.RequestsBypassApproval.Key, command.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(LeaveRequestErrors.BypassNotAllowed);
        }

        var valid = await rules.ValidateAsync(
            tenant.TenantId, command.EmployeeId, command.LeaveTypeId, command.StartDate, command.EndDate, command.Description, null, cancellationToken,
            requireSelfService: false, startTime: command.StartTime, endTime: command.EndTime, halfDayPeriod: command.HalfDayPeriod);
        if (valid.IsFailure)
        {
            return valid.Error;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var submitted = LeaveRequest.Submit(
            tenant.TenantId, command.EmployeeId, valid.Value.Type.Id, valid.Value.Type.Code, command.StartDate, command.EndDate, valid.Value.Days,
            command.Description, LeaveRequestSource.OnBehalf, now, currentUser.IpAddress, null, null, command.StartTime, command.EndTime, command.HalfDayPeriod);
        if (submitted.IsFailure)
        {
            return submitted.Error;
        }

        var request = submitted.Value;
        if (command.BypassApproval)
        {
            var approved = request.ApproveWithoutWorkflow(now);
            if (approved.IsFailure)
            {
                return approved.Error;
            }

            ledger.AddRange([LeaveLedgerEntry.Usage(request.TenantId, request.EmployeeId, request.LeaveTypeId, request.CalculatedDays, request.StartDate, LeaveLedgerSource.Request, request.Id)]);
        }
        else
        {
            var approval = await workflow.StartAsync(ApprovalRequestTypes.Leave, request.Id, command.EmployeeId, null, currentUser.IpAddress, null, cancellationToken);
            if (approval.IsFailure)
            {
                return approval.Error;
            }

            request.AttachApproval(approval.Value);
        }

        requests.Add(request);
        return LeaveRequestMapper.ToDto(request, valid.Value.Type.Name);
    }
}

/// <summary>Workflow approved a "Leave" request → Approved + Usage ledger row dated at the leave start date.</summary>
public sealed class LeaveApprovalCompletedHandler(ILeaveRequestRepository requests, ILeaveLedgerRepository ledger, ILeaveUnitOfWork unitOfWork, TimeProvider clock)
    : IIntegrationEventHandler<ApprovalCompleted>
{
    public async Task Handle(ApprovalCompleted integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.RequestType != ApprovalRequestTypes.Leave
            || await requests.FindByIdAsync(integrationEvent.SubjectId, cancellationToken) is not { } request
            || request.Approve(integrationEvent.ApprovalRequestId, clock.GetUtcNow().UtcDateTime).IsFailure)
        {
            return;
        }

        ledger.AddRange([LeaveLedgerEntry.Usage(request.TenantId, request.EmployeeId, request.LeaveTypeId, request.CalculatedDays, request.StartDate, LeaveLedgerSource.Request, request.Id)]);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Workflow rejected a "Leave" request → Rejected (its reservation disappears).</summary>
public sealed class LeaveApprovalRejectedHandler(ILeaveRequestRepository requests, ILeaveUnitOfWork unitOfWork, TimeProvider clock)
    : IIntegrationEventHandler<ApprovalRejected>
{
    public async Task Handle(ApprovalRejected integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.RequestType != ApprovalRequestTypes.Leave
            || await requests.FindByIdAsync(integrationEvent.SubjectId, cancellationToken) is not { } request
            || request.Reject(integrationEvent.ApprovalRequestId, clock.GetUtcNow().UtcDateTime).IsFailure)
        {
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

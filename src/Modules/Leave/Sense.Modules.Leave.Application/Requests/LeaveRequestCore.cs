using FluentValidation;
using Sense.Modules.Employee.Contracts;
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
using Sense.Shared.Contracts.Configuration;
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

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
    public const string AnnualMinimumBlock = "leave.errors.annual_minimum_block";
    public const string AboveMaxDays = "leave.errors.above_max_days";
    public const string InsufficientBalance = "leave.errors.insufficient_balance";
    public const string DescriptionRequired = "leave.errors.description_required";
    public const string NotOwner = "leave.request.not_owner";
    public const string BypassNotAllowed = "leave.errors.bypass_approval_not_allowed";
    public const string GenderNotEligible = "leave.errors.gender_not_eligible";
    public const string SubstituteRequired = "leave.errors.substitute_required";
    public const string SubstituteIsRequester = "leave.errors.substitute_is_requester";
    public const string SubstituteNotFound = "leave.errors.substitute_not_found";
    public const string SubstituteOnLeave = "leave.errors.substitute_on_leave";

    /// <summary>
    /// senseik-037: the request's end date falls in a year the effective holiday calendar has no religious
    /// (Ramazan/Kurban Bayramı) entries seeded for yet. Non-blocking - surfaced via LeaveRequestPreviewDto.Warnings,
    /// same as InsufficientBalance, so a future-dated request past the seeded years still goes through but flags the
    /// calendar as needing a refresh instead of silently allowing it (previously a live risk - see IHolidayCalendarLookup
    /// .GetLastDefinedReligiousHolidayYearAsync's doc comment).
    /// </summary>
    public const string HolidayCalendarYearUndefined = "leave.errors.holiday_calendar_year_undefined";
}

/// <summary>
/// Denetim raporu A6: whether an employee's recorded gender satisfies a type's <see cref="LeaveEligibleGender"/>. Only a
/// known Male/Female gender is ever restricted: an unrecorded (<c>Unspecified</c>), <c>Other</c> or unknown (null) gender
/// applies no restriction - the request then relies on the normal HR approval instead of blocking on missing data.
/// </summary>
public static class LeaveGenderEligibility
{
    public static bool IsEligible(LeaveEligibleGender required, EmployeeGender? gender) => required switch
    {
        LeaveEligibleGender.Female => gender is not EmployeeGender.Male,
        LeaveEligibleGender.Male => gender is not EmployeeGender.Female,
        _ => true,
    };
}

public sealed record LeaveRequestDto(
    Guid Id, Guid EmployeeId, Guid LeaveTypeId, LeaveTypeCode LeaveTypeCode, DateOnly StartDate, DateOnly EndDate, decimal CalculatedDays,
    string? Description, LeaveRequestStatus Status, LeaveRequestSource Source, Guid? ApprovalRequestId, int RevisionNo, DateTime SubmittedAt, DateTime? DecidedAt,
    string? LeaveTypeName = null, Guid? PreviousRequestId = null, DateTime? CancelledAt = null, string? CancellationReason = null,
    TimeOnly? StartTime = null, TimeOnly? EndTime = null, HalfDayPeriod? HalfDayPeriod = null, Guid? SubstituteEmployeeId = null);

internal static class LeaveRequestMapper
{
    public static LeaveRequestDto ToDto(LeaveRequest r, string? typeName = null) =>
        new(r.Id, r.EmployeeId, r.LeaveTypeId, r.LeaveTypeCodeSnapshot, r.StartDate, r.EndDate, r.CalculatedDays, r.Description, r.Status, r.Source,
            r.ApprovalRequestId, r.RevisionNo, r.SubmittedAt, r.DecidedAt, typeName, r.PreviousRequestId, r.CancelledAt, r.CancellationReason,
            r.StartTime, r.EndTime, r.HalfDayPeriod, r.SubstituteEmployeeId);

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

using System.Globalization;
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

/// <summary>Validation shared by submit and edit (11.3.5 "Talep doğrulama" steps 2, 4, 6, 7, 9, 11, 12).</summary>
public sealed class LeaveRequestRules(
    IEmployeeLeaveProfileRepository profiles,
    ILeaveRuleSetRepository ruleSets,
    ILeaveRequestRepository requests,
    ILeaveLedgerRepository ledger,
    ILeaveDayCalculator dayCalculator,
    LeaveBalanceService balances,
    LeaveBackdatePolicy backdatePolicy,
    IEmployeeLookup employees)
{
    public async Task<Result<(LeaveType Type, decimal Days)>> ValidateAsync(
        Guid tenantId, Guid employeeId, Guid leaveTypeId, DateOnly start, DateOnly end, string? description, Guid? editedRequestId, CancellationToken ct,
        bool requireSelfService = true, TimeOnly? startTime = null, TimeOnly? endTime = null, HalfDayPeriod? halfDayPeriod = null)
    {
        if (end < start)
        {
            return Error.Validation(LeaveDomainErrors.RequestInvalidRange);
        }

        // Only the employee's own request is bounded; HR entering leave on behalf (requireSelfService: false) may backdate freely.
        if (requireSelfService && start < await backdatePolicy.EarliestSelfServiceStartAsync(tenantId, ct))
        {
            return Error.Rule(LeaveRequestErrors.StartDateTooFarInPast, ("max", LeaveBackdatePolicy.SelfServiceMaxBackdateDays));
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

        if (type.EligibleGender != LeaveEligibleGender.Any
            && !LeaveGenderEligibility.IsEligible(type.EligibleGender, await employees.GetGenderAsync(tenantId, employeeId, ct)))
        {
            return Error.Validation(LeaveRequestErrors.GenderNotEligible);
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

        // Annual's MinConsecutiveDays is the m.56/3 "smallest block" (AnnualLeaveSplitRule), not a per-request minimum.
        if (type.MinConsecutiveDays is { } min && days < min && !AnnualLeaveSplitRule.Applies(type))
        {
            return Error.Validation(LeaveRequestErrors.BelowMinDays, ("min", min));
        }

        if (type.MaxConsecutiveDays is { } max && days > max)
        {
            return Error.Validation(LeaveRequestErrors.AboveMaxDays, ("max", max));
        }

        // HR entering leave on behalf (requireSelfService: false) is the employer's own decision to split the leave.
        var checkSplit = requireSelfService && AnnualLeaveSplitRule.Applies(type) && days < type.MinConsecutiveDays;
        if (type.HasBalance && (type.NegativeBalance != NegativeBalanceMode.Unlimited || checkSplit))
        {
            var entries = await ledger.ListAsync(employeeId, type.Id, ct);
            var projected = LeaveBalanceEngine.Project(type, profile.LeaveAccrualStartDate, balances.Today, entries);
            var pending = await requests.SumPendingDaysAsync(employeeId, type.Id, ct);
            if (editedRequestId is { } editedId && await requests.FindByIdAsync(editedId, ct) is { Status: LeaveRequestStatus.Pending } edited && edited.LeaveTypeId == type.Id)
            {
                pending -= edited.CalculatedDays;
            }

            var available = projected.AvailableBalance - pending;
            if (type.NegativeBalance != NegativeBalanceMode.Unlimited)
            {
                var floor = type.NegativeBalance == NegativeBalanceMode.Limited ? -(type.NegativeBalanceLimitDays ?? 0m) : 0m;
                if (available - days < floor)
                {
                    // Days carry 4 decimals in the ledger; show "73" / "72,5" instead of "73,0000".
                    return Error.Validation(LeaveRequestErrors.InsufficientBalance, ("available", available.ToString("0.##", CultureInfo.CurrentCulture)));
                }
            }

            if (checkSplit && AnnualLeaveSplitRule.WouldForfeitMinimumBlock(type.MinConsecutiveDays!.Value, days, available)
                && !await HasBookedBlockInPeriodAsync(employeeId, type, projected, editedRequestId, ct))
            {
                return Error.Validation(LeaveRequestErrors.AnnualMinimumBlock, ("min", type.MinConsecutiveDays!.Value));
            }
        }

        return (type, days);
    }

    /// <summary>
    /// Denetim raporu A5 "Vekil": a type with RequiresSubstitute needs one; any given substitute must be an active employee other
    /// than the requester and must not have active leave clashing with the requested dates (same partial-day rules as the requester's
    /// own overlap check). Kept apart from <see cref="ValidateAsync"/> so the live preview (which has no substitute yet) never warns.
    /// </summary>
    public async Task<Result> ValidateSubstituteAsync(
        Guid tenantId, Guid employeeId, LeaveType type, Guid? substituteEmployeeId, DateOnly start, DateOnly end,
        TimeOnly? startTime, TimeOnly? endTime, HalfDayPeriod? halfDayPeriod, CancellationToken ct)
    {
        if (substituteEmployeeId is not { } substituteId)
        {
            return type.RequiresSubstitute ? Error.Validation(LeaveRequestErrors.SubstituteRequired) : Result.Success();
        }

        if (substituteId == employeeId)
        {
            return Error.Validation(LeaveRequestErrors.SubstituteIsRequester);
        }

        if (!await employees.ExistsActiveAsync(tenantId, substituteId, ct))
        {
            return Error.Validation(LeaveRequestErrors.SubstituteNotFound);
        }

        var substituteLeave = await requests.ListActiveOverlappingAsync(substituteId, start, end, ct);
        return substituteLeave.Any(r => r.ConflictsWith(start, end, startTime, endTime, halfDayPeriod))
            ? Error.Conflict(LeaveRequestErrors.SubstituteOnLeave)
            : Result.Success();
    }

    private async Task<bool> HasBookedBlockInPeriodAsync(Guid employeeId, LeaveType type, LeaveBalance projected, Guid? editedRequestId, CancellationToken ct)
    {
        var booked = await requests.ListByEmployeeAsync(employeeId, ct);
        return AnnualLeaveSplitRule.HasBookedBlock(
            type.MinConsecutiveDays!.Value,
            booked
                .Where(r => r.LeaveTypeId == type.Id && r.Id != editedRequestId && (r.Status is LeaveRequestStatus.Pending or LeaveRequestStatus.Approved)
                    && r.StartDate >= projected.CurrentPeriodFrom && r.StartDate <= projected.CurrentPeriodTo)
                .Select(r => r.CalculatedDays));
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

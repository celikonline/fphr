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

/// <summary>Employee card → Devamsızlık kapsamı dışı: one peer (same org unit) with active leave overlapping the previewed dates.</summary>
public sealed record TeamOverlapEmployeeDto(Guid EmployeeId, string Name, DateOnly StartDate, DateOnly EndDate);

/// <summary>
/// "Ekip çakışma bilgisi" (faz-05 tur 3, S-7): non-blocking - the caller's own submit is never rejected because of
/// this, it is informational only ("takımınızdan N kişi bu tarihlerde izinli"). Only computed for the self-service
/// preview (<see cref="PreviewMyLeaveRequestQuery"/>); on-behalf preview leaves it null.
/// <see cref="OrgUnitName"/> (S-8, senseik-026) is the requester's org unit at resolve time - only populated for the
/// request-detail view (<see cref="Sense.Modules.Leave.Application.Requests.LeaveRequestDetailBuilder"/>); the preview
/// handlers above leave it null since the calendar grid there has no group heading to show.
/// </summary>
public sealed record TeamOverlapDto(int Count, IReadOnlyList<TeamOverlapEmployeeDto> Employees, string? OrgUnitName = null);

/// <summary>Live day count of a leave request draft; rule violations come back in <c>Warnings</c> instead of failing.</summary>
public sealed record LeaveRequestPreviewDto(decimal Days, int WeeklyRestDays, int PublicHolidayDays, IReadOnlyList<Error> Warnings, TeamOverlapDto? TeamOverlap = null, int OverlappingApprovedCount = 0);

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
    ILeaveDayCalculator dayCalculator, IEmployeeLookup employeeLookup, IEmployeeDirectory employees, ILeaveRequestRepository requests)
    : IQueryHandler<PreviewMyLeaveRequestQuery, LeaveRequestPreviewDto>
{
    /// <summary>Display cap only - <c>Count</c> still reports the true total overlap.</summary>
    private const int TeamOverlapMaxEmployees = 20;

    public async Task<Result<LeaveRequestPreviewDto>> Handle(PreviewMyLeaveRequestQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(LeaveErrors.NoEmployeeForUser);
        }

        var preview = await LeaveRequestPreview.BuildAsync(
            rules, profiles, ruleSets, dayCalculator, tenant.TenantId, employeeId, query.LeaveTypeId, query.StartDate, query.EndDate, query.EditedRequestId, true,
            cancellationToken, query.StartTime, query.EndTime, query.HalfDayPeriod);
        if (preview.IsFailure)
        {
            return preview;
        }

        var overlap = await BuildTeamOverlapAsync(employeeId, query.StartDate, query.EndDate, cancellationToken);
        var approvedCount = await GetApprovedOverlapCountAsync(employeeId, query.StartDate, cancellationToken);
        return preview.Value with { TeamOverlap = overlap, OverlappingApprovedCount = approvedCount };
    }

    /// <summary>Same org unit, active (Pending/Approved) leave overlapping [start, end], excluding the caller.</summary>
    private async Task<TeamOverlapDto?> BuildTeamOverlapAsync(Guid employeeId, DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        var orgUnitId = await employeeLookup.GetOrgUnitIdAsync(tenant.TenantId, employeeId, cancellationToken);
        if (orgUnitId is null)
        {
            return null;
        }

        var peers = (await employees.ListActiveEmployeeIdsInOrgUnitsAsync(tenant.TenantId, [orgUnitId.Value], cancellationToken))
            .Where(id => id != employeeId).ToList();
        if (peers.Count == 0)
        {
            return new TeamOverlapDto(0, []);
        }

        var overlapping = await requests.ListActiveInRangeAsync(start, end, peers, cancellationToken);
        if (overlapping.Count == 0)
        {
            return new TeamOverlapDto(0, []);
        }

        var names = (await employees.GetContactsAsync(tenant.TenantId, overlapping.Select(r => r.EmployeeId).Distinct().ToList(), cancellationToken))
            .ToDictionary(c => c.EmployeeId, c => $"{c.FirstName} {c.LastName}".Trim());

        var shown = overlapping
            .OrderBy(r => r.StartDate)
            .Take(TeamOverlapMaxEmployees)
            .Select(r => new TeamOverlapEmployeeDto(r.EmployeeId, names.GetValueOrDefault(r.EmployeeId, string.Empty), r.StartDate, r.EndDate))
            .ToList();
        return new TeamOverlapDto(overlapping.Count, shown);
    }

    /// <summary>Count of approved leave requests on the same start date from the same org unit, excluding the caller.</summary>
    private async Task<int> GetApprovedOverlapCountAsync(Guid employeeId, DateOnly date, CancellationToken cancellationToken)
    {
        var orgUnitId = await employeeLookup.GetOrgUnitIdAsync(tenant.TenantId, employeeId, cancellationToken);
        if (orgUnitId is null)
        {
            return 0;
        }

        var peers = (await employees.ListActiveEmployeeIdsInOrgUnitsAsync(tenant.TenantId, [orgUnitId.Value], cancellationToken))
            .Where(id => id != employeeId).ToList();
        if (peers.Count == 0)
        {
            return 0;
        }

        var approved = await requests.ListActiveInRangeAsync(date, date, peers, cancellationToken);
        return approved.Count(r => r.Status == LeaveRequestStatus.Approved);
    }
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

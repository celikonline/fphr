using Microsoft.EntityFrameworkCore;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Ledger;
using Sense.Modules.Leave.Domain.Overtime;
using Sense.Modules.Leave.Domain.Profiles;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Shared.Contracts.Legislation;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Leave.Infrastructure.Persistence;

public sealed class LeaveRuleSetRepository(LeaveDbContext context) : ILeaveRuleSetRepository
{
    public void Add(LeaveRuleSet ruleSet) => context.RuleSets.Add(ruleSet);

    public void Remove(LeaveRuleSet ruleSet) => context.RuleSets.Remove(ruleSet);

    public async Task<Result<LeaveRuleSet>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var set = await context.RuleSets.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        return set is null ? Error.NotFound(LeaveDomainErrors.RuleSetNotFound) : set;
    }

    public async Task<LeaveRuleSet?> FindDefaultAsync(CancellationToken cancellationToken = default) =>
        await context.RuleSets.FirstOrDefaultAsync(r => r.IsDefault, cancellationToken)
        ?? context.RuleSets.Local.FirstOrDefault(r => r.IsDefault);

    public async Task<IReadOnlyList<LeaveRuleSet>> ListAsync(CancellationToken cancellationToken = default) =>
        await context.RuleSets.OrderByDescending(r => r.IsDefault).ThenBy(r => r.Name).ToListAsync(cancellationToken);

    public Task<int> CountAssignedEmployeesAsync(Guid ruleSetId, CancellationToken cancellationToken = default) =>
        context.Profiles.CountAsync(p => p.RuleSetId == ruleSetId, cancellationToken);
}

public sealed class EmployeeLeaveProfileRepository(LeaveDbContext context) : IEmployeeLeaveProfileRepository
{
    public void Add(EmployeeLeaveProfile profile) => context.Profiles.Add(profile);

    public async Task<EmployeeLeaveProfile?> FindByEmployeeIdAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        await context.Profiles.FirstOrDefaultAsync(p => p.EmployeeId == employeeId, cancellationToken);
}

public sealed class LeaveLedgerRepository(LeaveDbContext context) : ILeaveLedgerRepository
{
    public void AddRange(IEnumerable<LeaveLedgerEntry> entries) => context.LedgerEntries.AddRange(entries);

    public void RemoveRange(IEnumerable<LeaveLedgerEntry> entries) => context.LedgerEntries.RemoveRange(entries);

    public async Task<IReadOnlyList<LeaveLedgerEntry>> ListAsync(Guid employeeId, Guid? leaveTypeId = null, CancellationToken cancellationToken = default)
    {
        var query = context.LedgerEntries.Where(e => e.EmployeeId == employeeId);
        if (leaveTypeId is { } typeId)
        {
            query = query.Where(e => e.LeaveTypeId == typeId);
        }

        return await query.OrderBy(e => e.EffectiveDate).ThenBy(e => e.CreatedAt).ToListAsync(cancellationToken);
    }
}

/// <summary>ILegislationParameterReader over leave.legislation_parameters (system-wide rows, no tenant filter).</summary>
public sealed class LegislationParameterReader(LeaveDbContext context) : ILegislationParameterReader
{
    public async Task<LegislationParameterValue?> FindAsync(
        string key, DateOnly asOf, string country = LegislationCountries.Turkey, CancellationToken cancellationToken = default) =>
        await context.LegislationParameters.AsNoTracking()
            .Where(p => p.Key == key && p.Country == country && p.EffectiveFrom <= asOf && (p.EffectiveTo == null || asOf <= p.EffectiveTo))
            .OrderByDescending(p => p.EffectiveFrom)
            .Select(p => new LegislationParameterValue(p.Key, p.Country, p.DecimalValue, p.ValueJson, p.EffectiveFrom, p.EffectiveTo, p.Source))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<LegislationParameterValue>> ListAsync(
        IReadOnlyCollection<string> keys, string country = LegislationCountries.Turkey, CancellationToken cancellationToken = default)
    {
        var keyList = keys.ToList();
        return await context.LegislationParameters.AsNoTracking()
            .Where(p => keyList.Contains(p.Key) && p.Country == country)
            .OrderBy(p => p.Key).ThenBy(p => p.EffectiveFrom)
            .Select(p => new LegislationParameterValue(p.Key, p.Country, p.DecimalValue, p.ValueJson, p.EffectiveFrom, p.EffectiveTo, p.Source))
            .ToListAsync(cancellationToken);
    }
}

public sealed class LeaveRequestRepository(LeaveDbContext context) : ILeaveRequestRepository
{
    public void Add(LeaveRequest request) => context.Requests.Add(request);

    public async Task<LeaveRequest?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.Requests.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<LeaveRequest>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        await context.Requests.Where(r => r.EmployeeId == employeeId).OrderByDescending(r => r.StartDate).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<LeaveRequest>> ListActiveOverlappingAsync(Guid employeeId, DateOnly start, DateOnly end, CancellationToken cancellationToken = default) =>
        await context.Requests
            .Where(r => r.EmployeeId == employeeId
                && (r.Status == LeaveRequestStatus.Pending || r.Status == LeaveRequestStatus.Approved)
                && r.StartDate <= end && start <= r.EndDate)
            .ToListAsync(cancellationToken);

    public async Task<decimal> SumPendingDaysAsync(Guid employeeId, Guid leaveTypeId, CancellationToken cancellationToken = default) =>
        await context.Requests
            .Where(r => r.EmployeeId == employeeId && r.LeaveTypeId == leaveTypeId && r.Status == LeaveRequestStatus.Pending)
            .SumAsync(r => r.CalculatedDays, cancellationToken);

    public async Task<IReadOnlyList<LeaveRequest>> ListActiveInRangeAsync(
        DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? employeeIds, CancellationToken cancellationToken = default)
    {
        var query = context.Requests.AsNoTracking()
            .Where(r => (r.Status == LeaveRequestStatus.Pending || r.Status == LeaveRequestStatus.Approved) && r.StartDate <= to && from <= r.EndDate);
        if (employeeIds is not null)
        {
            var ids = employeeIds.ToList();
            query = query.Where(r => ids.Contains(r.EmployeeId));
        }

        return await query.OrderBy(r => r.StartDate).ToListAsync(cancellationToken);
    }
}

public sealed class OvertimeRequestRepository(LeaveDbContext context) : IOvertimeRequestRepository
{
    public void Add(OvertimeRequest request) => context.OvertimeRequests.Add(request);

    public async Task<OvertimeRequest?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.OvertimeRequests.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<OvertimeRequest>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        await context.OvertimeRequests.Where(r => r.EmployeeId == employeeId)
            .OrderByDescending(r => r.WorkDate).ThenByDescending(r => r.StartTime).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<OvertimeRequest>> ListActiveAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        await context.OvertimeRequests
            .Where(r => r.EmployeeId == employeeId
                && (r.Status == OvertimeRequestStatus.Pending || r.Status == OvertimeRequestStatus.Approved)
                && r.WorkDate >= from && r.WorkDate <= to)
            .ToListAsync(cancellationToken);
}

public sealed class OvertimeConsentRepository(LeaveDbContext context) : IOvertimeConsentRepository
{
    public void Add(OvertimeConsent consent) => context.OvertimeConsents.Add(consent);

    public async Task<OvertimeConsent?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.OvertimeConsents.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<OvertimeConsent>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        await context.OvertimeConsents.Where(c => c.EmployeeId == employeeId).OrderByDescending(c => c.GivenAt).ToListAsync(cancellationToken);
}

public sealed class OvertimeConversionRepository(LeaveDbContext context) : IOvertimeConversionRepository
{
    public void Add(OvertimeConversion conversion) => context.OvertimeConversions.Add(conversion);

    public async Task<IReadOnlyList<OvertimeConversion>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        await context.OvertimeConversions.Where(c => c.EmployeeId == employeeId).OrderByDescending(c => c.ConvertedAt).ToListAsync(cancellationToken);
}

using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Balances;
using Sense.Modules.Leave.Domain.Ledger;
using Sense.Modules.Leave.Domain.Profiles;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Shared.Contracts.Legislation;

namespace Sense.Modules.Leave.Application.Balances;

/// <summary>Shared balance workflow for handlers: default rule set provisioning, recalculation and projection. Does not save.</summary>
public sealed class LeaveBalanceService(
    ILeaveRuleSetRepository ruleSets,
    ILeaveLedgerRepository ledger,
    ILegislationParameterReader legislation,
    TimeProvider clock)
{
    public DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    /// <summary>The tenant's default rule set; seeds "Örnek Türk İş Kanunu kuralı" (durations as of today) when the tenant has none.</summary>
    public async Task<LeaveRuleSet> EnsureDefaultRuleSetAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var existing = await ruleSets.FindDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var durations = await StatutoryLeaveResolver.ResolveAsync(legislation, Today, cancellationToken);
        var seeded = LeaveRuleSetSeed.CreateTurkishLabourLawRuleSet(tenantId, durations);
        ruleSets.Add(seeded);
        return seeded;
    }

    /// <summary>Regenerates Accrual/CarryOver rows of every PerYear type in the profile's rule set up to today.</summary>
    public async Task RecalculateAsync(EmployeeLeaveProfile profile, LeaveRuleSet ruleSet, CancellationToken cancellationToken)
    {
        var existing = await ledger.ListAsync(profile.EmployeeId, null, cancellationToken);
        var statutoryValues = await legislation.ListAsync(
            [LegislationParameterKeys.AnnualTier1Days, LegislationParameterKeys.AnnualTier2AfterYears, LegislationParameterKeys.AnnualTier2Days,
             LegislationParameterKeys.AnnualTier3AfterYears, LegislationParameterKeys.AnnualTier3Days, LegislationParameterKeys.AnnualYoungAgeMax,
             LegislationParameterKeys.AnnualOldAgeMin, LegislationParameterKeys.AnnualAgeExceptionMinDays],
            LegislationCountries.Turkey, cancellationToken);
        var statutory = StatutoryLeaveResolver.Resolve(statutoryValues, Today).Annual;

        foreach (var type in ruleSet.Types.Where(t => t.HasBalance))
        {
            var rows = existing.Where(e => e.LeaveTypeId == type.Id).ToList();
            ledger.RemoveRange(rows.Where(r => r.IsGenerated));
            var preserved = rows.Where(r => !r.IsGenerated).ToList();
            var generated = LeaveBalanceEngine.Regenerate(
                type, profile.TenantId, profile.EmployeeId, profile.LeaveAccrualStartDate, profile.BirthDate, Today, preserved, statutory);
            ledger.AddRange(generated);
        }
    }

    public async Task<IReadOnlyList<LeaveBalance>> ProjectAsync(EmployeeLeaveProfile profile, LeaveRuleSet ruleSet, CancellationToken cancellationToken)
    {
        var entries = await ledger.ListAsync(profile.EmployeeId, null, cancellationToken);
        return ruleSet.Types
            .Where(t => t.HasBalance && !t.IsDisabled)
            .OrderBy(t => t.SortOrder)
            .Select(t => LeaveBalanceEngine.Project(t, profile.LeaveAccrualStartDate, Today, entries.Where(e => e.LeaveTypeId == t.Id).ToList()))
            .ToList();
    }
}

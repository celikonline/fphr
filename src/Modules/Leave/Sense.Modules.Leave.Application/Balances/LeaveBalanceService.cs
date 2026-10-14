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

    /// <summary>
    /// Regenerates Accrual/CarryOver rows of every PerYear type in the profile's rule set up to today. <paramref name="unsavedEntries"/>:
    /// rows added in the current unit of work (not yet visible to the ledger query) that must count as preserved input.
    /// </summary>
    /// <param name="recalculateFrom">
    /// Optional start date: when given, only generated rows dated on or after it are replaced - earlier generated rows are
    /// left untouched in the ledger. The regeneration math itself still walks the whole history from the profile's
    /// <see cref="EmployeeLeaveProfile.LeaveAccrualStartDate"/> (each period's carry-over depends on the one before it), so
    /// the rows that do get rebuilt are identical to what a full recalculation would have produced for those same dates -
    /// omitting the parameter reproduces the previous, unchanged full-rebuild behavior.
    /// </param>
    public async Task RecalculateAsync(
        EmployeeLeaveProfile profile, LeaveRuleSet ruleSet, CancellationToken cancellationToken,
        IReadOnlyCollection<LeaveLedgerEntry>? unsavedEntries = null, DateOnly? recalculateFrom = null)
    {
        var existing = (await ledger.ListAsync(profile.EmployeeId, null, cancellationToken)).Concat(unsavedEntries ?? []).ToList();
        var statutoryValues = await legislation.ListAsync(
            [LegislationParameterKeys.AnnualTier1Days, LegislationParameterKeys.AnnualTier2AfterYears, LegislationParameterKeys.AnnualTier2Days,
             LegislationParameterKeys.AnnualTier3AfterYears, LegislationParameterKeys.AnnualTier3Days, LegislationParameterKeys.AnnualYoungAgeMax,
             LegislationParameterKeys.AnnualOldAgeMin, LegislationParameterKeys.AnnualAgeExceptionMinDays],
            LegislationCountries.Turkey, cancellationToken);
        var statutory = StatutoryLeaveResolver.Resolve(statutoryValues, Today).Annual;

        foreach (var type in ruleSet.Types.Where(t => t.HasBalance))
        {
            var rows = existing.Where(e => e.LeaveTypeId == type.Id).ToList();
            var generatedRows = rows.Where(r => r.IsGenerated).ToList();
            var toRemove = recalculateFrom is { } from ? generatedRows.Where(r => r.EffectiveDate >= from).ToList() : generatedRows;
            ledger.RemoveRange(toRemove);
            var preserved = rows.Where(r => !r.IsGenerated).ToList();
            var generated = LeaveBalanceEngine.Regenerate(
                type, profile.TenantId, profile.EmployeeId, profile.LeaveAccrualStartDate, profile.BirthDate, Today, preserved, statutory, recalculateFrom);
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

    /// <summary>Projects a historical/future balance without replacing today's persisted ledger or saving changes.</summary>
    public async Task<IReadOnlyList<LeaveBalance>> ProjectAsOfAsync(
        EmployeeLeaveProfile profile, LeaveRuleSet ruleSet, DateOnly asOf, CancellationToken cancellationToken)
    {
        var entries = await ledger.ListAsync(profile.EmployeeId, null, cancellationToken);
        var statutoryValues = await legislation.ListAsync(
            [LegislationParameterKeys.AnnualTier1Days, LegislationParameterKeys.AnnualTier2AfterYears, LegislationParameterKeys.AnnualTier2Days,
             LegislationParameterKeys.AnnualTier3AfterYears, LegislationParameterKeys.AnnualTier3Days, LegislationParameterKeys.AnnualYoungAgeMax,
             LegislationParameterKeys.AnnualOldAgeMin, LegislationParameterKeys.AnnualAgeExceptionMinDays],
            LegislationCountries.Turkey, cancellationToken);
        var statutory = StatutoryLeaveResolver.Resolve(statutoryValues, asOf).Annual;
        return ruleSet.Types.Where(t => t.HasBalance && !t.IsDisabled).OrderBy(t => t.SortOrder).Select(type =>
        {
            var preserved = entries.Where(e => e.LeaveTypeId == type.Id && !e.IsGenerated && e.EffectiveDate <= asOf).ToList();
            var generated = LeaveBalanceEngine.Regenerate(type, profile.TenantId, profile.EmployeeId,
                profile.LeaveAccrualStartDate, profile.BirthDate, asOf, preserved, statutory);
            return LeaveBalanceEngine.Project(type, profile.LeaveAccrualStartDate, asOf, preserved.Concat(generated).ToList());
        }).ToList();
    }
}

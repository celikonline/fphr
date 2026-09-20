namespace Sense.Modules.Leave.Domain.RuleSets;

/// <summary>
/// Brings a tenant's already-provisioned system-seed rule set ("Örnek Türk İş Kanunu kuralı") up to the current seed
/// (senseik-036): <c>LeaveBalanceService.EnsureDefaultRuleSetAsync</c> only seeds tenants that have no rule set at all,
/// so without this step existing tenants would never receive types added to the seed later. Custom rule sets
/// (<see cref="LeaveRuleSet.IsSystemSeed"/> false, i.e. HR-created or cloned) are never touched. Idempotent.
/// </summary>
public static class LeaveRuleSetSeedSync
{
    /// <summary>Adds seed types the set lacks and applies the known legacy-value upgrades; returns the number of changes.</summary>
    public static int Apply(LeaveRuleSet set, IReadOnlyCollection<LeaveTypeSettings> seedTypes)
    {
        if (!set.IsSystemSeed)
        {
            return 0;
        }

        var changes = 0;
        foreach (var seed in seedTypes.Where(t => t.Code != LeaveTypeCode.Custom))
        {
            var existing = set.Types.FirstOrDefault(t => t.Code == seed.Code);
            if (existing is null)
            {
                changes += set.AddType(seed).IsSuccess ? 1 : 0;
                continue;
            }

            var upgraded = Upgrade(existing.ToSettings(), seed);
            if (upgraded is not null && set.UpdateType(existing.Id, upgraded).IsSuccess)
            {
                changes++;
            }
        }

        return changes;
    }

    /// <summary>The seed's new settings for an existing type still carrying the pre-036 seed values; null when it was customised or is current.</summary>
    private static LeaveTypeSettings? Upgrade(LeaveTypeSettings current, LeaveTypeSettings seed) => current.Code switch
    {
        // 4857 md.56/3: "en az ardışık gün" (asgari blok) boştu; yalnız hiç doldurulmamışsa 10'a çekilir.
        LeaveTypeCode.Annual when current.MinConsecutiveDays is null && seed.MinConsecutiveDays is not null =>
            current with { MinConsecutiveDays = seed.MinConsecutiveDays },

        // 4857 md.27: iş arama izni "Limitsiz" idi; yalnız hâlâ o ilk seed halindeyse günlük 2 saat kuralına geçirilir.
        LeaveTypeCode.JobSearch when current.LimitType == LeaveLimitType.Unlimited && current.MinConsecutiveDays is null =>
            current with { LimitType = seed.LimitType, PerRequestMaxDays = seed.PerRequestMaxDays, MinConsecutiveDays = seed.MinConsecutiveDays },
        _ => null,
    };
}

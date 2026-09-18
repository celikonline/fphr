using Sense.Modules.Payroll.Domain.Calculation;
using Sense.Shared.Contracts.Legislation;

namespace Sense.Modules.Payroll.Application.Severance;

/// <summary>JSON shape of one Notice.WeeksBySeniorityTiers entry (ValueJson array).</summary>
public sealed record NoticeWeeksTierParameter(int? UpperBoundDays, int Weeks);

/// <summary>
/// Resolves the Severance.*/Notice.* LegislationParameter values (ADR 0008) into the plain
/// <see cref="SeveranceLegislationSnapshot"/> Domain's <see cref="SeveranceCalculator"/> takes - same
/// Application-resolves/Domain-takes-plain-values seam as <see cref="Calculation.PayrollLegislationSnapshotResolver"/>.
/// </summary>
public static class SeveranceLegislationSnapshotResolver
{
    public static async Task<SeveranceLegislationSnapshot> ResolveAsync(
        ILegislationParameterReader reader, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        var ceiling = await reader.GetDecimalAsync(LegislationParameterKeys.SeveranceCeilingGross, asOf, cancellationToken).ConfigureAwait(false)
            ?? throw MissingParameter(LegislationParameterKeys.SeveranceCeilingGross, asOf);

        var tiers = await reader.GetAsync<List<NoticeWeeksTierParameter>>(LegislationParameterKeys.NoticeWeeksBySeniorityTiers, asOf, cancellationToken).ConfigureAwait(false)
            ?? throw MissingParameter(LegislationParameterKeys.NoticeWeeksBySeniorityTiers, asOf);

        return new SeveranceLegislationSnapshot(
            ceiling,
            tiers.Select(t => new NoticeWeeksTier(t.UpperBoundDays, t.Weeks)).ToList(),
            asOf);
    }

    private static InvalidOperationException MissingParameter(string key, DateOnly asOf) =>
        new($"LegislationParameter '{key}' has no value effective on {asOf:yyyy-MM-dd} (ADR 0008 seed missing or not yet migrated).");
}

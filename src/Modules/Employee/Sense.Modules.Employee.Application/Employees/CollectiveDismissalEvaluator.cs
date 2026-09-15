using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Shared.Contracts.Legislation;

namespace Sense.Modules.Employee.Application.Employees;

/// <summary>
/// One row of the collective-dismissal threshold table (4857 md.29): the tenant's headcount picks a tier, and the tier
/// gives the number of dismissals that triggers the notification duty - either a flat <see cref="Count"/> or a
/// <see cref="Percent"/> of the headcount.
/// </summary>
public sealed record CollectiveDismissalTier(int MinHeadcount, int? MaxHeadcount, int? Count, decimal? Percent);

/// <summary>
/// Result of the collective-dismissal check ("Toplu işten çıkarma eşik uyarısı", 11.3.03 E5a). Advisory only: like the
/// job-security warning it never blocks a termination. <see cref="Threshold"/> is 0 when the tenant's headcount is
/// below the smallest tier, i.e. the rule does not apply at all (<see cref="InScope"/> false).
/// </summary>
public sealed record CollectiveDismissalEvaluation(
    bool InScope,
    bool ShowWarning,
    DateOnly WindowStart,
    DateOnly WindowEnd,
    int DismissalsInWindow,
    int Threshold,
    int Headcount,
    int NoticeDays,
    int RehirePriorityMonths,
    int TempWorkerBanMonths,
    string? WarningMessage);

/// <summary>
/// Counts employer-initiated dismissals in a rolling 30-day window and compares them with the statutory collective
/// dismissal threshold (4857 md.29). Thresholds come from CollectiveDismissal.* legislation parameters when the tenant's
/// installation has them; the statutory defaults below are used otherwise (same fallback pattern as JobSecurityEvaluator).
/// </summary>
/// <remarks>
/// Only <see cref="TerminationType.ValidCause_Art18"/> counts: md.24/25 (both sides' just cause), resignation, mutual
/// rescission (ikale/arabulucu anlaşması) and fixed-term expiry are excluded by the statute, and TerminationType.Other
/// is excluded deliberately because the seeded reason catalog maps death, retirement and military service to it.
/// </remarks>
public sealed class CollectiveDismissalEvaluator(IEmployeeRepository employees, ILegislationParameterReader legislation, TimeProvider clock)
{
    /// <summary>4857 md.29: 20-100 workers -> 10 dismissals, 101-300 -> 10%, 301+ -> 30.</summary>
    private static readonly CollectiveDismissalTier[] DefaultTiers =
    [
        new(20, 100, 10, null),
        new(101, 300, null, 10m),
        new(301, null, 30, null),
    ];

    private const int DefaultWindowDays = 30;
    private const int DefaultNoticeDays = 30;
    private const int DefaultRehirePriorityMonths = 6;
    private const int DefaultTempWorkerBanMonths = 8;

    private static readonly TerminationType[] CountedTypes = [TerminationType.ValidCause_Art18];

    /// <summary>
    /// Evaluates the window ending on <paramref name="asOf"/>. <paramref name="pendingType"/> is the termination about
    /// to be submitted (counted on top of the stored ones); null for the wizard's preview, which only reports history.
    /// </summary>
    public async Task<CollectiveDismissalEvaluation> EvaluateAsync(DateOnly asOf, TerminationType? pendingType, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var noticeDays = (int)await legislation.GetDecimalOrDefaultAsync(
            LegislationParameterKeys.CollectiveDismissalNoticeDays, today, DefaultNoticeDays, cancellationToken).ConfigureAwait(false);
        var rehirePriorityMonths = (int)await legislation.GetDecimalOrDefaultAsync(
            LegislationParameterKeys.CollectiveDismissalRehirePriorityMonths, today, DefaultRehirePriorityMonths, cancellationToken).ConfigureAwait(false);
        var tempWorkerBanMonths = (int)await legislation.GetDecimalOrDefaultAsync(
            LegislationParameterKeys.CollectiveDismissalTempWorkerBanMonths, today, DefaultTempWorkerBanMonths, cancellationToken).ConfigureAwait(false);
        var tiers = await legislation.GetAsync<CollectiveDismissalTier[]>(
            LegislationParameterKeys.CollectiveDismissalThresholds, today, cancellationToken).ConfigureAwait(false);

        var windowStart = asOf.AddDays(1 - DefaultWindowDays);
        var stored = await employees.CountTerminationsAsync(windowStart, asOf, CountedTypes, cancellationToken).ConfigureAwait(false);
        var pending = pendingType is { } type && CountedTypes.Contains(type) ? 1 : 0;
        var dismissals = stored + pending;

        var headcount = await employees.CountActiveAsync(cancellationToken).ConfigureAwait(false);
        var threshold = Threshold(tiers is { Length: > 0 } ? tiers : DefaultTiers, headcount);
        var showWarning = threshold > 0 && dismissals >= threshold;

        return new CollectiveDismissalEvaluation(
            threshold > 0,
            showWarning,
            windowStart,
            asOf,
            dismissals,
            threshold,
            headcount,
            noticeDays,
            rehirePriorityMonths,
            tempWorkerBanMonths,
            showWarning
                ? $"Son {DefaultWindowDays} günde {dismissals} işçi çıkarıldı ve toplu işten çıkarma eşiği ({threshold}) aşıldı: " +
                  $"İŞKUR'a, işyeri sendika temsilcilerine ve Bölge Müdürlüğü'ne en az {noticeDays} gün önceden yazılı bildirim yapılmalıdır. " +
                  $"Çıkarılan işçilerin {rehirePriorityMonths} ay içinde yeniden işe alımda önceliği vardır; aynı işlerde {tempWorkerBanMonths} ay geçici iş ilişkisi kurulamaz."
                : null);
    }

    /// <summary>The dismissal count that triggers the duty for this headcount; 0 when no tier covers it (the rule does not apply).</summary>
    private static int Threshold(IReadOnlyList<CollectiveDismissalTier> tiers, int headcount)
    {
        var tier = tiers.FirstOrDefault(t => headcount >= t.MinHeadcount && (t.MaxHeadcount is null || headcount <= t.MaxHeadcount));
        if (tier is null)
        {
            return 0;
        }

        return tier.Count ?? (tier.Percent is { } percent ? (int)Math.Ceiling(headcount * percent / 100m) : 0);
    }
}

using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Terminations;

namespace Sense.Modules.Employee.Application.Terminations;

/// <summary>
/// One-time reconciliation for tenants that seeded the termination reason catalog before the SGK exit code table
/// was verified (senseik-006/F-01, docs/handoffs/sgk-exit-codes.md §1/§5). <see cref="TerminationReasonProvisioningService"/>
/// only seeds a tenant's catalog once (on first read/write); a tenant that opened the wizard before this fix has the
/// old wrong codes written permanently into its own copy of the seed. This service corrects only system rows
/// (<c>IsSystem == true</c>) whose <see cref="TerminationReason.DefaultSgkExitCode"/> still equals the specific
/// old-known-wrong value from before the fix - a tenant's own edit to a system row (tenants may rename/repoint any
/// row, including system ones) is left untouched, and a row already corrected (by a previous run, or created after
/// the fix) is a no-op. Idempotent: safe to run any number of times, any number of tenants.
/// Deliberately does NOT touch already-terminated employees' own Employee.SgkExitCode snapshot - that is a
/// point-in-time legal fact recorded at the time of exit, not a lookup value, and correcting it in bulk risks
/// contradicting a real SGK beyanı already filed (handoff §5 risk 1 - a human/business decision, not this service's).
/// Caller decides when/how this runs (e.g. a one-off admin trigger per tenant) - not wired into module startup.
/// </summary>
public sealed class TerminationReasonCorrectionService(ITerminationReasonRepository reasons)
{
    /// <summary>Reason code -> (old wrong value written by the pre-fix seed, corrected value; null clears the field).</summary>
    private static readonly IReadOnlyDictionary<string, (string OldValue, string? NewValue)> KnownCorrections =
        new Dictionary<string, (string OldValue, string? NewValue)>(StringComparer.Ordinal)
        {
            ["Death"] = ("21", "10"),
            ["MilitaryService"] = ("10", "12"),
            ["Resignation"] = ("29", "03"),
            ["EmployerValidCause"] = ("02", "04"),

            // Family types (md.24/md.25): the old single-value default is cleared, not replaced by another guess -
            // there is no safe single default across 23/24/25 or 26/27/28 (see SgkExitCodeRules).
            ["EmployeeJustCause"] = ("19", null),
            ["EmployerJustCause"] = ("15", null),

            ["FixedTermExpiry"] = ("03", "05"),
            ["MarriageFemale"] = ("04", "13"),
            ["GroupTransfer"] = ("27", "16"),
            ["Other"] = ("13", "22"),

            // MutualRescission, Retirement, ProbationTermination were already correct pre-fix - no entry needed.
        };

    /// <summary>Applies the corrections to the current tenant's catalog (per ambient ITenantContext); returns the number of rows changed.</summary>
    public async Task<int> CorrectKnownWrongSystemCodesAsync(CancellationToken cancellationToken = default)
    {
        var systemRows = await reasons.ListSystemRowsAsync(cancellationToken);
        var correctedCount = 0;

        foreach (var row in systemRows)
        {
            if (!KnownCorrections.TryGetValue(row.Code, out var fix))
            {
                continue;
            }

            if (!string.Equals(row.DefaultSgkExitCode, fix.OldValue, StringComparison.Ordinal))
            {
                // Already corrected, or the tenant changed it themselves - either way, not ours to touch.
                continue;
            }

            row.Update(row.Name, fix.NewValue, row.DefaultTerminationType, row.SortOrder);
            correctedCount++;
        }

        return correctedCount;
    }
}

using System.Text.RegularExpressions;

namespace Sense.Modules.Documents.Application.PayslipImports;

public enum PayslipEmployeeMatchKind
{
    Active,
    Terminated,
    Unknown,
}

public sealed record PayslipEmployeeMatch(PayslipEmployeeMatchKind Kind, Guid? EmployeeId)
{
    public static PayslipEmployeeMatch Unknown { get; } = new(PayslipEmployeeMatchKind.Unknown, null);
}

/// <summary>Resolves a registration number (sicil numarası) to an employee; implemented over Employee.Contracts in Infrastructure.</summary>
public interface IPayslipEmployeeResolver
{
    Task<PayslipEmployeeMatch> ResolveAsync(Guid tenantId, string registrationNumber, CancellationToken cancellationToken = default);
}

/// <summary>A PDF that passed the name/content checks: <c>&lt;sicil&gt;_&lt;YYYY-MM&gt;.pdf</c> (K8).</summary>
public sealed record PayslipCandidate(string FileName, string RegistrationNumber, int Year, int Month, byte[] Content);

public enum PayslipMatchStatus
{
    Matched,
    Unmatched,
    Terminated,
    Duplicate,
}

public sealed record PayslipMatchOutcome(PayslipCandidate Candidate, PayslipMatchStatus Status, Guid? EmployeeId);

/// <summary>Reason codes used in the import report (stable strings the UI localizes).</summary>
public static class PayslipImportReasons
{
    public const string NotPdf = "not_pdf";
    public const string InvalidFileName = "invalid_file_name";
    public const string EntryTooLarge = "zip_entry_too_large";
    public const string ZipCorrupt = "zip_corrupt";
    public const string TooManyEntries = "zip_too_many_entries";
    public const string UnknownRegistration = "unknown_registration";
    public const string TerminatedEmployee = "terminated_employee";
    public const string DuplicateRegistration = "duplicate_registration";
    public const string DeliveryFailed = "delivery_failed";
}

/// <summary>Parses payslip file names and matches them to employees by registration number (K8).</summary>
public sealed partial class PayslipFileMatcher(IPayslipEmployeeResolver resolver)
{
    private const int MinYear = 2000;
    private const int MaxYear = 2100;

    [GeneratedRegex(@"^(?<sicil>[A-Za-z0-9.\-]+)_(?<year>\d{4})-(?<month>0[1-9]|1[0-2])\.pdf$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    /// <summary>Parses <c>&lt;sicil&gt;_&lt;YYYY-MM&gt;.pdf</c>; null when the name does not follow the pattern.</summary>
    public static (string RegistrationNumber, int Year, int Month)? ParseFileName(string fileName)
    {
        var m = NamePattern().Match(fileName);
        if (!m.Success)
        {
            return null;
        }

        var year = int.Parse(m.Groups["year"].Value, System.Globalization.CultureInfo.InvariantCulture);
        if (year is < MinYear or > MaxYear)
        {
            return null;
        }

        return (m.Groups["sicil"].Value, year, int.Parse(m.Groups["month"].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Matches every candidate. Two candidates for the same (registration number, period) are ambiguous: both are marked
    /// <see cref="PayslipMatchStatus.Duplicate"/> and neither is imported (Review Focus #6). Registration numbers compare
    /// case-insensitively.
    /// </summary>
    public async Task<IReadOnlyList<PayslipMatchOutcome>> MatchAsync(
        Guid tenantId, IReadOnlyList<PayslipCandidate> candidates, CancellationToken cancellationToken = default)
    {
        var duplicateKeys = candidates
            .GroupBy(c => (c.RegistrationNumber.ToUpperInvariant(), c.Year, c.Month))
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();

        var resolved = new Dictionary<string, PayslipEmployeeMatch>(StringComparer.OrdinalIgnoreCase);
        var outcomes = new List<PayslipMatchOutcome>(candidates.Count);
        foreach (var candidate in candidates)
        {
            if (duplicateKeys.Contains((candidate.RegistrationNumber.ToUpperInvariant(), candidate.Year, candidate.Month)))
            {
                outcomes.Add(new PayslipMatchOutcome(candidate, PayslipMatchStatus.Duplicate, null));
                continue;
            }

            if (!resolved.TryGetValue(candidate.RegistrationNumber, out var match))
            {
                match = await resolver.ResolveAsync(tenantId, candidate.RegistrationNumber, cancellationToken);
                resolved[candidate.RegistrationNumber] = match;
            }

            outcomes.Add(match.Kind switch
            {
                PayslipEmployeeMatchKind.Active when match.EmployeeId is { } id => new PayslipMatchOutcome(candidate, PayslipMatchStatus.Matched, id),
                PayslipEmployeeMatchKind.Terminated => new PayslipMatchOutcome(candidate, PayslipMatchStatus.Terminated, match.EmployeeId),
                _ => new PayslipMatchOutcome(candidate, PayslipMatchStatus.Unmatched, null),
            });
        }

        return outcomes;
    }
}

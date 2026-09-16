namespace Sense.Modules.Reporting.Contracts;

public sealed record ReportFileResult(string FileName, string ContentType, byte[] Content);

/// <summary>
/// A generatable report, contributed by any module (Documents, Employee, ...) via a DI registration of this
/// interface. Reporting enumerates all registered providers (<c>IEnumerable&lt;IReportProvider&gt;</c>) to build its
/// catalog and dispatch generation - implementing modules reference this Contracts project, never the reverse
/// (Modules_TalkOnlyThroughContracts).
/// </summary>
public interface IReportProvider
{
    /// <summary>Stable key, e.g. "documents.missingDocuments"; the web catalog looks up its display name/description
    /// by this key in the "reports" i18n namespace rather than carrying localized text over the wire.</summary>
    string Key { get; }

    /// <summary>Extra permission the caller must hold beyond the base <c>reports.*</c> gate; null = none.</summary>
    string? RequiredPermission { get; }

    Task<ReportFileResult> GenerateAsync(Guid tenantId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken);
}

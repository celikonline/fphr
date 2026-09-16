namespace Sense.Modules.Leave.Contracts;

/// <summary>
/// Narrow cross-module projection of one absence record - date, kind ("Unexcused"/"Excused"/"Late", the C# enum
/// name, not the API's camelCase JSON) and hours (mainly meaningful for "Late"). Reversal rows and originals a
/// reversal has already offset are excluded by the reader; only currently-active absences come back.
/// </summary>
public sealed record AbsenceRecordSummary(DateOnly Date, string Kind, decimal? Hours);

/// <summary>
/// Leave's read port for other modules that need to treat absences as missing days (Payroll/Faz 7's likely
/// consumer; not wired to any consumer yet - the bridge is Faz 7's job, per faz-05 tur 3 "Payroll'u bağlama").
/// </summary>
public interface IAbsenceReader
{
    Task<IReadOnlyList<AbsenceRecordSummary>> ListAsync(
        Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}

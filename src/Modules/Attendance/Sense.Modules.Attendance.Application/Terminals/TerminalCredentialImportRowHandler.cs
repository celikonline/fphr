using Microsoft.Extensions.Options;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Terminals;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Import;
using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Attendance.Application.Terminals;

/// <summary>
/// "terminal-credentials" satırlarını uygular (Z5.4). Önce her satır doğrulanır (sicil → çalışan, işten ayrılmış mı, tür, PIN/uzunluk biçimi) ve
/// değer HMAC ile özetlenir; ham değer yalnız bu döngüde bellekte kalır - satır sonucuna, hata argümanına, loga yazılmaz. Uygulama
/// <see cref="TerminalCredentialRules.Assign"/> ile: kart/harici için yeni → Inserted, pasif kimlik devri/yeniden etkinleştirme → Updated, aynı
/// çalışanda zaten aktif → Skipped (idempotent); PIN için bu üçü tek sonuçtur → Processed (PIN'in önceden var olup olmadığı sızmasın).
/// Başka çalışanda aktif → satır hatası (sahip bilgisi yok; PIN'de genel rejected). PIN ret sayısı
/// <see cref="TerminalCredentialImportOptions.MaxPinRejectsPerImport"/>'u aşarsa iş bütünüyle <see cref="TerminalCredentialErrors.ImportAborted"/>
/// ile düşer (hiçbir şey kaydedilmez, satır ayrıntısı yok, kalan satırlar denenmez). Çağıranın attendance.terminal.manage veri kapsamı
/// dışındaki çalışan <see cref="TerminalCredentialErrors.EmployeeNotFound"/> (varlık/işten ayrılma bilgisi sızmaz). Dosya içindeki tekrarlar da
/// aynı kurala tabidir. AllOrNothing'de herhangi bir hata hiçbir şeyin kaydedilmemesi demektir; aksi halde geçerli satırlar tek kayıtta yazılır.
/// </summary>
public sealed class TerminalCredentialImportRowHandler(
    ITenantContext tenant, IEmployeeLookup employees, IDataScopeService dataScope, ITerminalRepository terminals, ITerminalCredentialHasher hasher,
    IOptions<TerminalCredentialImportOptions> options, IAttendanceUnitOfWork unitOfWork)
    : IImportRowHandler
{
    public string TemplateCode => TerminalCredentialImportColumns.TemplateCode;

    public async Task<ImportBatchResult> ProcessAsync(ImportBatch batch, CancellationToken cancellationToken = default)
    {
        if (!hasher.IsAvailable)
        {
            // DataTransfer checks the template's readiness first (503 for the whole request); this only guards a direct call.
            return new ImportBatchResult(
                batch.Rows.Select(r => Failed(r.RowNumber, null, null, TerminalErrors.CredentialPepperMissing)).ToList(), Applied: false);
        }

        var results = new Dictionary<int, ImportRowResult>();
        var plans = new List<CredentialRowPlan>();
        var employeeCache = new Dictionary<string, EmployeeSicilMatch?>(StringComparer.Ordinal);
        var visible = await dataScope.GetVisibleEmployeeIdsAsync(AttendancePermissions.TerminalManage.Key, cancellationToken);
        foreach (var row in batch.Rows)
        {
            var plan = await ValidateAsync(row, employeeCache, visible, cancellationToken);
            if (plan.Result is { } failed)
            {
                results[row.RowNumber] = failed;
                continue;
            }

            plans.Add(plan.Plan!);
        }

        if (batch.FailurePolicy == ImportFailurePolicy.AllOrNothing && results.Count > 0)
        {
            foreach (var plan in plans)
            {
                results[plan.RowNumber] = new ImportRowResult(plan.RowNumber, ImportRowOutcome.Skipped, plan.Sicil, null, []);
            }

            return new ImportBatchResult(Ordered(batch, results), Applied: false);
        }

        var (anyChange, aborted) = await ApplyAsync(plans, results, cancellationToken);
        if (aborted)
        {
            // Too many PIN rejections (bulk probing): tracked changes are never saved, no row detail is disclosed.
            return new ImportBatchResult([], Applied: false, FailureCode: TerminalCredentialErrors.ImportAborted);
        }

        if (batch.FailurePolicy == ImportFailurePolicy.AllOrNothing && results.Values.Any(r => r.Outcome == ImportRowOutcome.Error))
        {
            // Changes are only tracked, never saved: nothing is persisted; successful rows are reported as skipped.
            foreach (var done in results.Values.Where(r => r.Outcome is ImportRowOutcome.Inserted or ImportRowOutcome.Updated or ImportRowOutcome.Processed).ToList())
            {
                results[done.RowNumber] = done with { Outcome = ImportRowOutcome.Skipped, TargetEntityId = null };
            }

            return new ImportBatchResult(Ordered(batch, results), Applied: false);
        }

        if (anyChange)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new ImportBatchResult(Ordered(batch, results), Applied: true);
    }

    private async Task<(CredentialRowPlan? Plan, ImportRowResult? Result)> ValidateAsync(
        ImportRow row, Dictionary<string, EmployeeSicilMatch?> employeeCache, IReadOnlySet<Guid>? visible, CancellationToken cancellationToken)
    {
        var errors = new List<ImportRowError>();
        var sicil = row.Get(TerminalCredentialImportColumns.SicilNumarasi)?.Text?.Trim();
        if (string.IsNullOrEmpty(sicil))
        {
            errors.Add(new ImportRowError(TerminalCredentialImportColumns.SicilNumarasi, ImportErrorCodes.RequiredMissing));
        }

        var kind = ParseKind(row, errors);
        var value = row.Get(TerminalCredentialImportColumns.Value)?.Text?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            errors.Add(new ImportRowError(TerminalCredentialImportColumns.Value, ImportErrorCodes.RequiredMissing));
        }
        else if (kind is { } parsedKind && TerminalCredentialRules.FormatError(parsedKind, value) is { } formatError)
        {
            errors.Add(new ImportRowError(TerminalCredentialImportColumns.Value, formatError));
        }

        var employeeId = await ResolveEmployeeAsync(sicil, employeeCache, visible, errors, cancellationToken);
        if (errors.Count > 0 || employeeId is null || kind is null || string.IsNullOrEmpty(value))
        {
            return (null, new ImportRowResult(row.RowNumber, ImportRowOutcome.Error, sicil, null, errors));
        }

        var digest = hasher.Hash(tenant.TenantId, kind.Value, value);
        return digest.IsFailure
            ? (null, Failed(row.RowNumber, sicil, null, digest.Error.Code))
            : (new CredentialRowPlan(row.RowNumber, sicil!, employeeId.Value, kind.Value, digest.Value), null);
    }

    private async Task<(bool AnyChange, bool Aborted)> ApplyAsync(
        List<CredentialRowPlan> plans, Dictionary<int, ImportRowResult> results, CancellationToken cancellationToken)
    {
        if (plans.Count == 0)
        {
            return (false, false);
        }

        var maxPinRejects = Math.Max(1, options.Value.MaxPinRejectsPerImport);
        var pinRejects = 0;
        var digests = plans.Select(p => p.Digest).Distinct(StringComparer.Ordinal).ToList();
        var known = (await terminals.ListCredentialsByValuesAsync(digests, cancellationToken)).ToDictionary(c => (c.Kind, c.Value));
        var anyChange = false;
        foreach (var plan in plans)
        {
            var assigned = TerminalCredentialRules.Assign(
                terminals, tenant.TenantId, plan.EmployeeId, plan.Kind, plan.Digest, known.GetValueOrDefault((plan.Kind, plan.Digest)));
            if (assigned.IsFailure)
            {
                if (plan.Kind == TerminalCredentialKind.Pin && ++pinRejects > maxPinRejects)
                {
                    return (anyChange, true);
                }

                results[plan.RowNumber] = Failed(plan.RowNumber, plan.Sicil, TerminalCredentialImportColumns.Value, assigned.Error.Code);
                continue;
            }

            var (outcome, credential) = assigned.Value;
            known[(plan.Kind, plan.Digest)] = credential;
            anyChange |= outcome != TerminalCredentialAssignment.Unchanged;
            results[plan.RowNumber] = new ImportRowResult(plan.RowNumber, OutcomeOf(plan.Kind, outcome), plan.Sicil, credential.Id, []);
        }

        return (anyChange, false);
    }

    private async Task<Guid?> ResolveEmployeeAsync(
        string? sicil, Dictionary<string, EmployeeSicilMatch?> cache, IReadOnlySet<Guid>? visible, List<ImportRowError> errors, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(sicil))
        {
            return null;
        }

        if (!cache.TryGetValue(sicil, out var match))
        {
            match = await employees.FindBySicilNumarasiAsync(tenant.TenantId, sicil, cancellationToken);
            cache[sicil] = match;
        }

        // Kapsam dışı çalışan yokmuş gibi (işten ayrılmış olup olmadığı da sızmaz).
        if (match is null || (visible is not null && !visible.Contains(match.EmployeeId)))
        {
            errors.Add(new ImportRowError(TerminalCredentialImportColumns.SicilNumarasi, TerminalCredentialErrors.EmployeeNotFound));
            return null;
        }

        if (match.IsTerminated)
        {
            errors.Add(new ImportRowError(TerminalCredentialImportColumns.SicilNumarasi, TerminalCredentialErrors.EmployeeNotActive));
            return null;
        }

        return match.EmployeeId;
    }

    private static TerminalCredentialKind? ParseKind(ImportRow row, List<ImportRowError> errors)
    {
        var token = row.Get(TerminalCredentialImportColumns.Kind)?.Text?.Trim();
        if (string.IsNullOrEmpty(token))
        {
            errors.Add(new ImportRowError(TerminalCredentialImportColumns.Kind, ImportErrorCodes.RequiredMissing));
            return null;
        }

        var kind = TerminalCredentialImportColumns.KindOf(token);
        if (kind is null)
        {
            errors.Add(new ImportRowError(TerminalCredentialImportColumns.Kind, TerminalCredentialErrors.KindInvalid));
        }

        return kind;
    }

    /// <summary>PIN: tek genel sonuç (eklendi/devredildi/zaten vardı ayrımı PIN'in varlığını ima ederdi); kart/harici ayrıntılı.</summary>
    private static ImportRowOutcome OutcomeOf(TerminalCredentialKind kind, TerminalCredentialAssignment outcome)
    {
        if (kind == TerminalCredentialKind.Pin)
        {
            return ImportRowOutcome.Processed;
        }

        return outcome switch
        {
            TerminalCredentialAssignment.Created => ImportRowOutcome.Inserted,
            TerminalCredentialAssignment.Reassigned => ImportRowOutcome.Updated,
            _ => ImportRowOutcome.Skipped,
        };
    }

    /// <summary>Hata satırı: yalnız satırın kendi sicili ve hata kodu - başka çalışan/kimlik bilgisi ya da değer yok.</summary>
    private static ImportRowResult Failed(int rowNumber, string? sicil, string? column, string code) =>
        new(rowNumber, ImportRowOutcome.Error, sicil, null, [new ImportRowError(column, code)]);

    private static IReadOnlyList<ImportRowResult> Ordered(ImportBatch batch, Dictionary<int, ImportRowResult> results) =>
        batch.Rows.Where(r => results.ContainsKey(r.RowNumber)).Select(r => results[r.RowNumber]).ToList();
}

/// <summary>Doğrulanmış satır: yalnız özet taşır, ham değer yok. <see cref="ToString"/> özeti de yazmaz.</summary>
internal sealed record CredentialRowPlan(int RowNumber, string Sicil, Guid EmployeeId, TerminalCredentialKind Kind, string Digest)
{
    public override string ToString() => $"{nameof(CredentialRowPlan)} {{ {nameof(RowNumber)} = {RowNumber}, {nameof(Kind)} = {Kind} }}";
}

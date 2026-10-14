using Microsoft.Extensions.Options;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Terminals;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.Terminals;

/// <summary>Okutma kalemindeki çalışan kimliği: tür + terminalin okuduğu ham değer (yalnız bellekte; özetlenerek aranır).</summary>
public sealed record TerminalCredentialInput(TerminalCredentialKind? Kind, string? Value)
{
    public override string ToString() => $"{nameof(TerminalCredentialInput)} {{ {nameof(Kind)} = {Kind} }}";
}

/// <summary>Bir okutma: <see cref="Direction"/> yoksa (ya da unknown) terminalin yön moduna göre çözülür.</summary>
public sealed record TerminalPunchItem(
    TerminalCredentialInput? Credential, DateTime OccurredAtUtc, PunchDirectionDto? Direction, string? SourceRef = null);

public enum TerminalPunchItemStatus
{
    Accepted,
    Ignored,
    Rejected,
}

public sealed record TerminalPunchItemResultDto(int Index, TerminalPunchItemStatus Status, string? Reason = null);

public sealed record TerminalPunchBatchResultDto(int Accepted, int Ignored, int Rejected, IReadOnlyList<TerminalPunchItemResultDto> Items);

/// <summary><see cref="PinEntryBlocked"/>: bu terminalde PIN girişi kaba kuvvet koruması nedeniyle geçici olarak reddediliyor.</summary>
public sealed record TerminalHeartbeatDto(DateTime ServerTimeUtc, bool PinEntryBlocked = false);

/// <summary>
/// Terminal okutma ucu (Z5.1). Kiracı ve terminal yalnız kimliği doğrulanmış <see cref="TerminalIdentity"/>'den gelir. Her kalemin kimliği
/// kiracı içinde HMAC özetiyle aktif bir kimliğe eşlenir; eşlenemeyen/bozuk kalem kalem bazında reddedilir, diğerleri işlenir. Kalemler
/// <see cref="IPunchRecorder.RecordBatchAsync"/>'e TEK TEK verilir: sözleşme yalnız toplam sayıları döndürdüğünden kalem bazlı durum
/// (kabul/yoksayıldı/reddedildi) ancak böyle kesin bilinir ve bir kalemin hatası (ör. gelecekteki okutma) diğerlerini geri almaz.
/// Kimliği doğru olsa da çalışanı aktif olmayan (işten ayrılmış/askıda/taslak) kalem <c>employee_not_active</c> ile reddedilir; PIN kalemlerinde
/// tüm kimlik başarısızlıkları <c>credential_rejected</c>'tır ve terminal başına bütçeyle sınırlıdır (<see cref="TerminalCredentialFailureMonitor"/>);
/// engel açıkken TÜM PIN kalemleri (doğru/yanlış ayrımı olmadan) <c>pin_entry_blocked</c> alır.
/// <see cref="TerminalIngestOptions.MaxPunchAgeDays"/>'ten eski kalem <c>too_old</c>.
/// Recorder her çağrıda kendi değişikliklerini kaydeder; bu servis komut hattına (tek işlem) sokulmaz. Aynı okutmanın tekrar
/// gönderilmesi hata değil "ignored" döner (idempotans recorder'dadır).
/// </summary>
public sealed class TerminalPunchIngest(
    ITerminalRepository terminals, ITerminalCredentialHasher hasher, IEmployeeContactLookup employees, IPunchRecorder recorder,
    TerminalCredentialFailureMonitor pinFailures, IOptions<TerminalIngestOptions> options, TimeProvider clock)
{
    public const int MaxItemsPerRequest = 500;

    /// <summary>Yalnız bu durumdaki çalışanın okutması kabul edilir (EmployeeStatus.Active; taslak/askıda/işten ayrılmış reddedilir).</summary>
    private const string ActiveEmployeeStatus = "Active";

    public Task<Result<TerminalPunchBatchResultDto>> IngestAsync(
        TerminalIdentity terminal, IReadOnlyList<TerminalPunchItem?>? items, CancellationToken cancellationToken) =>
        IngestAsync(terminal, items, PunchSourceDto.Terminal, cancellationToken);

    public async Task<Result<TerminalPunchBatchResultDto>> IngestAsync(
        TerminalIdentity terminal, IReadOnlyList<TerminalPunchItem?>? items, PunchSourceDto source, CancellationToken cancellationToken)
    {
        if (items is null || items.Count == 0 || items.Count > MaxItemsPerRequest
            || source is not (PunchSourceDto.Terminal or PunchSourceDto.DeviceIntegration))
        {
            return Error.Validation(TerminalErrors.BatchSizeInvalid, ("max", MaxItemsPerRequest));
        }

        var now = await MarkSeenAsync(terminal, cancellationToken).ConfigureAwait(false);

        // Kimlik çözümü pepper ister: yoksa istek düzeyinde 503 (anahtar doğrulaması ve son görülme yine yapılmıştır).
        if (!hasher.IsAvailable)
        {
            return Error.ServiceUnavailable(TerminalErrors.CredentialPepperMissing);
        }

        // Engel açıksa PIN kalemleri hiçbir çözüm sorgusuna girmez (özet yok, kimlik/çalışan araması yok): doğru ve yanlış aday için yapılan iş -
        // dolayısıyla yanıt süresi - aynıdır. Engel bu istekte (bütçe dolunca) açılırsa, o istekte engelden SONRAKİ PIN kalemleri zaten çözülmüştür;
        // bu kalemlerin yanıtı aynıdır (pin_entry_blocked) ama isteğin süresi yine adaylara bağlıdır → sızıntı en çok engele giren tek istek
        // (pencere başına bir istek) ile sınırlıdır.
        var pinBlocked = pinFailures.IsBlocked(terminal.TerminalId);
        var oldest = now.AddDays(-Math.Max(1, options.Value.MaxPunchAgeDays));
        var prepared = new List<Prepared>(items.Count);
        foreach (var item in items)
        {
            if (!IsWellFormed(item))
            {
                prepared.Add(new Prepared(null, TerminalPunchRejectReasons.InvalidItem));
                continue;
            }

            if (NormalizeOccurredAt(item!.OccurredAtUtc) < oldest)
            {
                prepared.Add(new Prepared(null, TerminalPunchRejectReasons.TooOld));
                continue;
            }

            if (pinBlocked && item.Credential!.Kind == TerminalCredentialKind.Pin)
            {
                prepared.Add(new Prepared(null, TerminalPunchRejectReasons.PinEntryBlocked));
                continue;
            }

            var digest = hasher.Hash(terminal.TenantId, item.Credential!.Kind!.Value, item.Credential.Value!);
            if (digest.IsFailure)
            {
                return digest.Error;
            }

            prepared.Add(new Prepared(digest.Value, null));
        }

        var digests = prepared.Select(p => p.Digest).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        IReadOnlyList<EmployeeTerminalCredential> credentials = digests.Count == 0
            ? []
            : await terminals.ListActiveCredentialsByValuesAsync(terminal.TenantId, digests, cancellationToken).ConfigureAwait(false);
        var owners = credentials
            .Where(c => c.TenantId == terminal.TenantId && c.IsActive)
            .ToDictionary(c => (c.Kind, c.Value), c => c.EmployeeId);
        var activeEmployees = await ActiveEmployeesAsync(terminal.TenantId, owners.Values, cancellationToken).ConfigureAwait(false);

        var results = new List<TerminalPunchItemResultDto>(items.Count);
        for (var index = 0; index < items.Count; index++)
        {
            results.Add(await RecordAsync(terminal, index, items[index], prepared[index], owners, activeEmployees, source, cancellationToken).ConfigureAwait(false));
        }

        return new TerminalPunchBatchResultDto(
            results.Count(r => r.Status == TerminalPunchItemStatus.Accepted),
            results.Count(r => r.Status == TerminalPunchItemStatus.Ignored),
            results.Count(r => r.Status == TerminalPunchItemStatus.Rejected),
            results);
    }

    /// <summary>Nabız: son görülme güncellenir; cihaz saatini eşitlemesi için sunucu saati döner.</summary>
    public async Task<Result<TerminalHeartbeatDto>> HeartbeatAsync(TerminalIdentity terminal, CancellationToken cancellationToken)
    {
        var now = await MarkSeenAsync(terminal, cancellationToken).ConfigureAwait(false);
        return new TerminalHeartbeatDto(now, pinFailures.IsBlocked(terminal.TerminalId));
    }

    /// <summary>Yön verilmemişse: yalnız giriş turnikesi → In, yalnız çıkış → Out, iki yönlü → Unknown (gün hesaplanamaz olur).</summary>
    public static PunchDirectionDto ResolveDirection(PunchDirectionDto? given, TerminalDirectionMode mode) =>
        given is PunchDirectionDto.In or PunchDirectionDto.Out
            ? given.Value
            : mode switch
            {
                TerminalDirectionMode.InOnly => PunchDirectionDto.In,
                TerminalDirectionMode.OutOnly => PunchDirectionDto.Out,
                _ => PunchDirectionDto.Unknown,
            };

    /// <summary>UTC'ye çevirip mikrosaniyeye kırpar (.NET 100 ns, PostgreSQL 1 µs): aynı okutmanın yeniden denemesi aynı anahtara düşer.</summary>
    public static DateTime NormalizeOccurredAt(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
        return new DateTime(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerMicrosecond), DateTimeKind.Utc);
    }

    private static bool IsWellFormed(TerminalPunchItem? item) =>
        item is { Credential: { Kind: { } kind } credential, OccurredAtUtc: var occurred }
        && Enum.IsDefined(kind)
        && !string.IsNullOrWhiteSpace(credential.Value)
        && credential.Value.Trim().Length <= EmployeeTerminalCredential.ValueMaxLength
        && occurred != default;

    /// <summary>Kimliği çözülen çalışanlardan aktif olanlar - tek toplu sorgu (N+1 yok).</summary>
    private async Task<IReadOnlySet<Guid>> ActiveEmployeesAsync(Guid tenantId, IEnumerable<Guid> employeeIds, CancellationToken cancellationToken)
    {
        var ids = employeeIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var contacts = await employees.GetContactsAsync(tenantId, ids, cancellationToken).ConfigureAwait(false);
        return contacts
            .Where(c => string.Equals(c.Status, ActiveEmployeeStatus, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.EmployeeId)
            .ToHashSet();
    }

    private async Task<DateTime> MarkSeenAsync(TerminalIdentity terminal, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await terminals.MarkTerminalSeenAsync(terminal.TenantId, terminal.TerminalId, now, cancellationToken).ConfigureAwait(false);
        return now;
    }

    private async Task<TerminalPunchItemResultDto> RecordAsync(
        TerminalIdentity terminal, int index, TerminalPunchItem? item, Prepared prepared,
        IReadOnlyDictionary<(TerminalCredentialKind Kind, string Value), Guid> owners, IReadOnlySet<Guid> activeEmployees,
        PunchSourceDto source, CancellationToken cancellationToken)
    {
        if (prepared.Digest is not { } digest)
        {
            return new TerminalPunchItemResultDto(index, TerminalPunchItemStatus.Rejected, prepared.RejectReason);
        }

        // Buraya gelen kalemler istek başında toplu çözülmüştür (istek başında engel açıksa PIN kalemleri buraya hiç gelmez).
        var kind = item!.Credential!.Kind!.Value;
        var resolved = owners.TryGetValue((kind, digest), out var employeeId);
        var failure = !resolved ? TerminalPunchRejectReasons.UnknownCredential
            : !activeEmployees.Contains(employeeId) ? TerminalPunchRejectReasons.EmployeeNotActive
            : null;

        if (kind == TerminalCredentialKind.Pin)
        {
            // Engel bu istekte açıldıysa sonraki PIN kalemleri aynı yanıtı alır ve bütçeye yazılmaz (doğru PIN ne yanıttan ne engel süresinden
            // sızar; süre farkı yalnız bu tek istekte - bkz. IngestAsync).
            switch (pinFailures.Evaluate(terminal, digest, isFailure: failure is not null))
            {
                case PinGateOutcome.Blocked:
                    return new TerminalPunchItemResultDto(index, TerminalPunchItemStatus.Rejected, TerminalPunchRejectReasons.PinEntryBlocked);
                case PinGateOutcome.Failed:
                    return new TerminalPunchItemResultDto(index, TerminalPunchItemStatus.Rejected, TerminalPunchRejectReasons.CredentialRejected);
            }
        }
        else if (failure is not null)
        {
            return new TerminalPunchItemResultDto(index, TerminalPunchItemStatus.Rejected, failure);
        }

        var input = new PunchInput(
            terminal.TenantId, employeeId, NormalizeOccurredAt(item.OccurredAtUtc), ResolveDirection(item.Direction, terminal.DirectionMode),
            source, item.SourceRef, terminal.TerminalId, terminal.LocationId, Latitude: null, Longitude: null,
            IsManual: false, CreatedByUserId: null, Reason: null);
        var recorded = await recorder.RecordBatchAsync([input], cancellationToken).ConfigureAwait(false);
        if (recorded.IsFailure)
        {
            return new TerminalPunchItemResultDto(index, TerminalPunchItemStatus.Rejected, recorded.Error.Code);
        }

        return recorded.Value switch
        {
            { Accepted: > 0 } => new TerminalPunchItemResultDto(index, TerminalPunchItemStatus.Accepted),
            { Ignored: > 0 } => new TerminalPunchItemResultDto(index, TerminalPunchItemStatus.Ignored),
            { Rejected: > 0 } => new TerminalPunchItemResultDto(index, TerminalPunchItemStatus.Rejected, TerminalPunchRejectReasons.PeriodLocked),
            _ => new TerminalPunchItemResultDto(index, TerminalPunchItemStatus.Rejected, TerminalPunchRejectReasons.NotRecorded),
        };
    }

    /// <summary>Kalemin ön işlemesi: kimlik özeti ya da (bozuk/çok eski kalemde) red nedeni.</summary>
    private sealed record Prepared(string? Digest, string? RejectReason);
}

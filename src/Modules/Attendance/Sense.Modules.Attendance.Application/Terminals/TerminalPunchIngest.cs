using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Terminals;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.Terminals;

/// <summary>Okutma kalemindeki çalışan kimliği: tür + terminalin okuduğu ham değer (yalnız bellekte; özetlenerek aranır).</summary>
public sealed record TerminalCredentialInput(TerminalCredentialKind? Kind, string? Value)
{
    public override string ToString() => $"{nameof(TerminalCredentialInput)} {{ {nameof(Kind)} = {Kind} }}";
}

/// <summary>Bir okutma: <see cref="Direction"/> yoksa (ya da unknown) terminalin yön moduna göre çözülür.</summary>
public sealed record TerminalPunchItem(TerminalCredentialInput? Credential, DateTime OccurredAtUtc, PunchDirectionDto? Direction);

public enum TerminalPunchItemStatus
{
    Accepted,
    Ignored,
    Rejected,
}

public sealed record TerminalPunchItemResultDto(int Index, TerminalPunchItemStatus Status, string? Reason = null);

public sealed record TerminalPunchBatchResultDto(int Accepted, int Ignored, int Rejected, IReadOnlyList<TerminalPunchItemResultDto> Items);

public sealed record TerminalHeartbeatDto(DateTime ServerTimeUtc);

/// <summary>
/// Terminal okutma ucu (Z5.1). Kiracı ve terminal yalnız kimliği doğrulanmış <see cref="TerminalIdentity"/>'den gelir. Her kalemin kimliği
/// kiracı içinde HMAC özetiyle aktif bir kimliğe eşlenir; eşlenemeyen/bozuk kalem kalem bazında reddedilir, diğerleri işlenir. Kalemler
/// <see cref="IPunchRecorder.RecordBatchAsync"/>'e TEK TEK verilir: sözleşme yalnız toplam sayıları döndürdüğünden kalem bazlı durum
/// (kabul/yoksayıldı/reddedildi) ancak böyle kesin bilinir ve bir kalemin hatası (ör. gelecekteki okutma) diğerlerini geri almaz.
/// Recorder her çağrıda kendi değişikliklerini kaydeder; bu servis komut hattına (tek işlem) sokulmaz. Aynı okutmanın tekrar
/// gönderilmesi hata değil "ignored" döner (idempotans recorder'dadır).
/// </summary>
public sealed class TerminalPunchIngest(ITerminalRepository terminals, ITerminalCredentialHasher hasher, IPunchRecorder recorder, TimeProvider clock)
{
    public const int MaxItemsPerRequest = 500;

    public async Task<Result<TerminalPunchBatchResultDto>> IngestAsync(
        TerminalIdentity terminal, IReadOnlyList<TerminalPunchItem?>? items, CancellationToken cancellationToken)
    {
        if (items is null || items.Count == 0 || items.Count > MaxItemsPerRequest)
        {
            return Error.Validation(TerminalErrors.BatchSizeInvalid, ("max", MaxItemsPerRequest));
        }

        await MarkSeenAsync(terminal, cancellationToken).ConfigureAwait(false);

        var digests = items.Select(item => IsWellFormed(item) ? hasher.Hash(terminal.TenantId, item!.Credential!.Kind!.Value, item.Credential.Value!) : null).ToList();
        var credentials = await terminals.ListActiveCredentialsByValuesAsync(
            terminal.TenantId, digests.OfType<string>().Distinct(StringComparer.Ordinal).ToList(), cancellationToken).ConfigureAwait(false);
        var employees = credentials
            .Where(c => c.TenantId == terminal.TenantId && c.IsActive)
            .ToDictionary(c => (c.Kind, c.Value), c => c.EmployeeId);

        var results = new List<TerminalPunchItemResultDto>(items.Count);
        for (var index = 0; index < items.Count; index++)
        {
            results.Add(await RecordAsync(terminal, index, items[index], digests[index], employees, cancellationToken).ConfigureAwait(false));
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
        return new TerminalHeartbeatDto(now);
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

    private async Task<DateTime> MarkSeenAsync(TerminalIdentity terminal, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await terminals.MarkTerminalSeenAsync(terminal.TenantId, terminal.TerminalId, now, cancellationToken).ConfigureAwait(false);
        return now;
    }

    private async Task<TerminalPunchItemResultDto> RecordAsync(
        TerminalIdentity terminal, int index, TerminalPunchItem? item, string? digest,
        IReadOnlyDictionary<(TerminalCredentialKind Kind, string Value), Guid> employees, CancellationToken cancellationToken)
    {
        if (digest is null)
        {
            return new TerminalPunchItemResultDto(index, TerminalPunchItemStatus.Rejected, TerminalPunchRejectReasons.InvalidItem);
        }

        if (!employees.TryGetValue((item!.Credential!.Kind!.Value, digest), out var employeeId))
        {
            return new TerminalPunchItemResultDto(index, TerminalPunchItemStatus.Rejected, TerminalPunchRejectReasons.UnknownCredential);
        }

        var input = new PunchInput(
            terminal.TenantId, employeeId, NormalizeOccurredAt(item.OccurredAtUtc), ResolveDirection(item.Direction, terminal.DirectionMode),
            PunchSourceDto.Terminal, SourceRef: null, terminal.TerminalId, terminal.LocationId, Latitude: null, Longitude: null,
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
}

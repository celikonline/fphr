using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Domain.Terminals;

/// <summary>Terminalin kabul ettiği okutma yönü (Z5.1): iki yönlü, yalnız giriş ya da yalnız çıkış turnikesi.</summary>
public enum TerminalDirectionMode
{
    Both = 0,
    InOnly = 1,
    OutOnly = 2,
}

/// <summary>
/// Kayıtlı okutma terminali (zaman-devam Z5.1, spec §4). Terminal API anahtarıyla kimlik doğrular: yalnız anahtarın
/// özeti (<see cref="ApiKeyHash"/>) ve arama için kısa, gizli olmayan öneki (<see cref="ApiKeyPrefix"/>) saklanır;
/// anahtarın üretimi ve özetlenmesi uygulama katmanındadır. Kod tenant içinde tekildir.
/// </summary>
public sealed class Terminal : TenantAggregateRoot<Guid>, IAuditLogged
{
    public const int CodeMaxLength = 50;
    public const int ApiKeyHashMaxLength = 200;
    public const int ApiKeyPrefixMaxLength = 16;

    private Terminal()
    {
    }

    private Terminal(Guid id, Guid tenantId)
        : base(id, tenantId) => IsActive = true;

    /// <summary>Anahtar özeti denetim kaydında maskelenir.</summary>
    public static IReadOnlySet<string> SensitiveFields { get; } = new HashSet<string>(StringComparer.Ordinal) { nameof(ApiKeyHash) };

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public Guid? TerminalGroupId { get; private set; }

    public Guid? LocationId { get; private set; }

    public TerminalDirectionMode DirectionMode { get; private set; }

    public string ApiKeyHash { get; private set; } = string.Empty;

    public string ApiKeyPrefix { get; private set; } = string.Empty;

    public DateTime? LastSeenAtUtc { get; private set; }

    public bool IsActive { get; private set; }

    public static Result<Terminal> Create(
        Guid tenantId, string code, string name, Guid? terminalGroupId, Guid? locationId, TerminalDirectionMode directionMode, string apiKeyHash, string apiKeyPrefix)
    {
        var terminal = new Terminal(Guid.CreateVersion7(), tenantId);
        var details = terminal.Update(code, name, terminalGroupId, locationId, directionMode);
        if (details.IsFailure)
        {
            return details.Error;
        }

        var key = terminal.RotateApiKey(apiKeyHash, apiKeyPrefix);
        return key.IsFailure ? key.Error : terminal;
    }

    public Result Update(string code, string name, Guid? terminalGroupId, Guid? locationId, TerminalDirectionMode directionMode)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > CodeMaxLength)
        {
            return Error.Validation(AttendanceDomainErrors.TerminalCodeRequired);
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > AttendanceLimits.NameMaxLength || !Enum.IsDefined(directionMode))
        {
            return Error.Validation(AttendanceDomainErrors.TerminalNameRequired);
        }

        Code = code.Trim();
        Name = name.Trim();
        TerminalGroupId = terminalGroupId;
        LocationId = locationId;
        DirectionMode = directionMode;
        return Result.Success();
    }

    /// <summary>Yeni anahtar: eski anahtar hemen geçersizleşir (yalnız özet karşılaştırılır).</summary>
    public Result RotateApiKey(string apiKeyHash, string apiKeyPrefix)
    {
        if (string.IsNullOrWhiteSpace(apiKeyHash) || apiKeyHash.Length > ApiKeyHashMaxLength
            || string.IsNullOrWhiteSpace(apiKeyPrefix) || apiKeyPrefix.Length > ApiKeyPrefixMaxLength)
        {
            return Error.Validation(AttendanceDomainErrors.TerminalApiKeyRequired);
        }

        ApiKeyHash = apiKeyHash;
        ApiKeyPrefix = apiKeyPrefix;
        return Result.Success();
    }

    public void MarkSeen(DateTime nowUtc) => LastSeenAtUtc = nowUtc;

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}

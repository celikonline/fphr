using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Domain.Terminals;

/// <summary>Terminalde çalışanı tanıtan kimlik türü (Z5.4).</summary>
public enum TerminalCredentialKind
{
    Card = 0,
    Pin = 1,
    ExternalId = 2,
}

/// <summary>
/// Çalışanın terminal kimliği (zaman-devam Z5.4, spec §4): kart numarası, PIN ya da cihazdaki dış kimlik.
/// <see cref="Value"/> ham değer DEĞİL, uygulama katmanının ürettiği deterministik HMAC-SHA256 özetidir (sunucu tarafı pepper, T1 kararı;
/// bkz. Application <c>ITerminalCredentialHasher</c>) - düz metin kart/PIN hiçbir türde saklanmaz. Özet tenant içinde tür başına tekildir
/// (veritabanı indeksi pasif kayıtları da kapsar); terminal okutması özetle çalışana eşlenir. Pasif bir kimlik başka çalışana
/// <see cref="Reassign"/> ile yeniden bağlanır (yeni kayıt açılmaz).
/// </summary>
public sealed class EmployeeTerminalCredential : TenantAggregateRoot<Guid>, IAuditLogged
{
    public const int ValueMaxLength = 200;

    private EmployeeTerminalCredential()
    {
    }

    private EmployeeTerminalCredential(Guid id, Guid tenantId, Guid employeeId, TerminalCredentialKind kind, string value)
        : base(id, tenantId)
    {
        EmployeeId = employeeId;
        Kind = kind;
        Value = value;
        IsActive = true;
    }

    /// <summary>
    /// Hassas alan işareti. Denetim kaydı (AuditLogInterceptor) yalnız <c>AuditedFields</c> listesindeki alanları diff'ler; bu varlık o listeyi
    /// tanımlamaz, dolayısıyla <see cref="Value"/> hiçbir denetim ayrıntısına girmez (düz metin zaten saklanmaz).
    /// </summary>
    public static IReadOnlySet<string> SensitiveFields { get; } = new HashSet<string>(StringComparer.Ordinal) { nameof(Value) };

    public Guid EmployeeId { get; private set; }

    public TerminalCredentialKind Kind { get; private set; }

    public string Value { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public static Result<EmployeeTerminalCredential> Create(Guid tenantId, Guid employeeId, TerminalCredentialKind kind, string value)
    {
        if (employeeId == Guid.Empty)
        {
            return Error.Validation(AttendanceDomainErrors.PunchEmployeeRequired);
        }

        if (!Enum.IsDefined(kind) || string.IsNullOrWhiteSpace(value) || value.Trim().Length > ValueMaxLength)
        {
            return Error.Validation(AttendanceDomainErrors.TerminalCredentialValueRequired);
        }

        return new EmployeeTerminalCredential(Guid.CreateVersion7(), tenantId, employeeId, kind, value.Trim());
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    /// <summary>
    /// Pasif kimliği (ör. kaybedilip geri alınan kart) bir çalışana yeniden bağlar ve etkinleştirir. Tekil indeks pasif kayıtları da
    /// kapsadığından yeni kayıt yerine bu yol kullanılır; aktif bir kimlik sessizce başka çalışana geçirilemez - önce pasife alınmalıdır.
    /// </summary>
    public Result Reassign(Guid employeeId)
    {
        if (employeeId == Guid.Empty)
        {
            return Error.Validation(AttendanceDomainErrors.PunchEmployeeRequired);
        }

        if (IsActive)
        {
            return Error.Conflict(AttendanceDomainErrors.TerminalCredentialInUse);
        }

        EmployeeId = employeeId;
        IsActive = true;
        return Result.Success();
    }
}

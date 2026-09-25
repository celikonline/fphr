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
/// <see cref="Value"/> tenant içinde tür başına tekildir (veritabanı indeksi); terminal okutması bu değerle çalışana
/// eşlenir. PIN'in düz metin mi özet mi saklanacağı uygulama katmanının (T2) kararıdır; bu varlık değeri olduğu gibi tutar.
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

    /// <summary>Kimlik değeri denetim kaydında maskelenir.</summary>
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
}

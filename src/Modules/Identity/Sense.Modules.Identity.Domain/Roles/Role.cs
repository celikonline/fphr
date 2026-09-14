using System.Globalization;
using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Identity.Domain.Roles;

/// <summary>
/// Kiracıya ait rol. Sistem rolleri her kiracıya seed edilir (IsSystem=true, Code sabit) ve silinemez;
/// sistem rollerinin görünen adı kaynak anahtarından (roles.{code}.name) yerelleştirilir, Name alanı özel roller içindir.
/// </summary>
public sealed class Role : TenantAggregateRoot<Guid>, IAuditLogged
{
    private readonly List<RolePermission> _permissions = [];

    private Role()
    {
    }

    private Role(Guid id, Guid tenantId, string code, string? name, bool isSystem) : base(id, tenantId)
    {
        Code = code;
        Name = name;
        IsSystem = isSystem;
    }

    /// <summary>Sistem rollerinde sabit (Owner, Admin, HR…); özel rollerde tenant içinde unique kısa ad.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>Özel rol adı; sistem rollerinde null (yerelleştirilir).</summary>
    public string? Name { get; private set; }

    public string? Description { get; private set; }

    public bool IsSystem { get; private set; }

    /// <summary>Owner rolü izin düzenlemesine kapalıdır.</summary>
    public bool IsLocked => IsSystem && Code == SystemRoleCodes.Owner;

    public IReadOnlyCollection<RolePermission> Permissions => _permissions.AsReadOnly();

    public static Result<Role> CreateCustom(Guid tenantId, string code, string name, string? description)
    {
        var c = Guard.NotEmpty(code);
        if (!IsValidCode(c))
        {
            return Error.Validation(IdentityDomainErrors.RoleInvalidCode);
        }

        if (SystemRoleCodes.All.Contains(c, StringComparer.OrdinalIgnoreCase))
        {
            return Error.Conflict(IdentityDomainErrors.RoleSystemCode);
        }

        return new Role(Guid.CreateVersion7(), tenantId, c, Guard.MaxLength(Guard.NotEmpty(name), IdentityLimits.NameMaxLength), isSystem: false)
        {
            Description = description?.Trim(),
        };
    }

    public static Role CreateSystem(Guid tenantId, string code) => new(Guid.CreateVersion7(), tenantId, code, null, isSystem: true);

    public static bool IsValidCode(string code) =>
        code.Length <= IdentityLimits.RoleCodeMaxLength && code.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-');

    public Result Rename(string name, string? description)
    {
        if (IsSystem)
        {
            return Error.Rule(IdentityDomainErrors.RoleSystemReadOnly);
        }

        Name = Guard.MaxLength(Guard.NotEmpty(name), IdentityLimits.NameMaxLength);
        Description = description?.Trim();
        return Result.Success();
    }

    public Result SetPermissions(IEnumerable<RolePermission> permissions)
    {
        if (IsLocked)
        {
            return Error.Rule(IdentityDomainErrors.RoleLocked);
        }

        ReplacePermissions(permissions);
        return Result.Success();
    }

    /// <summary>Seed/migrasyon: kilitli rol dahil izinleri yeniden yazar.</summary>
    public void ReplacePermissionsUnchecked(IEnumerable<RolePermission> permissions) => ReplacePermissions(permissions);

    public Result EnsureDeletable(int assignedUserCount)
    {
        if (IsSystem)
        {
            return Error.Rule(IdentityDomainErrors.RoleSystemReadOnly);
        }

        if (assignedUserCount > 0)
        {
            return Error.Rule(IdentityDomainErrors.RoleInUse, (IdentityDomainErrors.Args.Count, assignedUserCount.ToString(CultureInfo.InvariantCulture)));
        }

        return Result.Success();
    }

    private void ReplacePermissions(IEnumerable<RolePermission> permissions)
    {
        _permissions.Clear();
        foreach (var p in permissions.GroupBy(p => p.Permission).Select(g => g.First()))
        {
            _permissions.Add(new RolePermission(Id, p.Permission, p.Scope, p.ScopeIdsJson));
        }

        Raise(new RolePermissionsChanged(Id, TenantId));
    }
}

/// <summary>Rol-izin ataması + veri kapsamı (Self/Team/Department/Location/Company/Custom).</summary>
public sealed class RolePermission : AuditableRecord
{
    private RolePermission()
    {
    }

    public RolePermission(Guid roleId, string permission, int scope, string? scopeIdsJson = null)
    {
        RoleId = roleId;
        Permission = Guard.NotEmpty(permission);
        Scope = scope;
        ScopeIdsJson = scopeIdsJson;
    }

    public Guid RoleId { get; private set; }

    public string Permission { get; private set; } = string.Empty;

    /// <summary>Sense.Shared.Contracts.Security.DataScope enum değeri (Domain, Contracts'a bağımlı olmadığı için int).</summary>
    public int Scope { get; private set; }

    /// <summary>Scope=Custom ise birim/lokasyon kimlikleri (jsonb dizi).</summary>
    public string? ScopeIdsJson { get; private set; }
}

public sealed record RolePermissionsChanged(Guid RoleId, Guid TenantId) : DomainEvent;

/// <summary>Varsayılan kullanıcı tipleri (sistem rolleri). Tam izin eşlemesi Application/SystemRoleDefinitions'ta.</summary>
public static class SystemRoleCodes
{
    public const string Owner = "Owner";
    public const string Admin = "Admin";
    public const string HR = "HR";
    public const string HRSpecialist = "HRSpecialist";
    public const string PayrollSpecialist = "PayrollSpecialist";
    public const string Finance = "Finance";
    public const string Manager = "Manager";
    public const string Timekeeper = "Timekeeper";
    public const string Recruiter = "Recruiter";
    public const string Interviewer = "Interviewer";
    public const string Auditor = "Auditor";
    public const string ExternalAccountant = "ExternalAccountant";
    public const string ITAdmin = "ITAdmin";
    public const string Employee = "Employee";
    public const string Intern = "Intern";
    public const string Contractor = "Contractor";
    public const string ApiIntegration = "ApiIntegration";

    public static readonly IReadOnlyList<string> All =
    [
        Owner, Admin, HR, HRSpecialist, PayrollSpecialist, Finance, Manager, Timekeeper, Recruiter, Interviewer,
        Auditor, ExternalAccountant, ITAdmin, Employee, Intern, Contractor, ApiIntegration,
    ];

    /// <summary>Yerelleştirme anahtarları: roles.{code}.name / roles.{code}.description</summary>
    public static string NameKey(string code) => string.Concat(ResourcePrefix, code.ToLowerInvariant(), NameSuffix);

    public static string DescriptionKey(string code) => string.Concat(ResourcePrefix, code.ToLowerInvariant(), DescriptionSuffix);

    private const string ResourcePrefix = "roles.";
    private const string NameSuffix = ".name";
    private const string DescriptionSuffix = ".description";
}

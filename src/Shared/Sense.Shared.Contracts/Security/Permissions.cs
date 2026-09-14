namespace Sense.Shared.Contracts.Security;

/// <summary>İznin geçerli olduğu veri kapsamı. Rol-izin atamasında belirlenir; en geniş kapsam kazanır.</summary>
public enum DataScope
{
    /// <summary>Yalnızca kendi kaydı.</summary>
    Self = 0,

    /// <summary>Kendisi + hiyerarşide altındaki çalışanlar.</summary>
    Team = 1,

    /// <summary>Kendi birimi ve alt birimleri.</summary>
    Department = 2,

    /// <summary>Aynı lokasyon.</summary>
    Location = 3,

    /// <summary>Tüm kiracı.</summary>
    Company = 4,

    /// <summary>Seçilen birim/lokasyon listesi.</summary>
    Custom = 5,
}

/// <summary>Birden fazla rolden gelen kapsamların birleştirme kuralı (enum sırası genişlik sırası değildir).</summary>
public static class DataScopes
{
    /// <summary>Genişlik sırası: Self &lt; Custom &lt; Team &lt; Department &lt; Location &lt; Company.</summary>
    public static int Rank(DataScope scope) => scope switch
    {
        DataScope.Self => 0,
        DataScope.Custom => 1,
        DataScope.Team => 2,
        DataScope.Department => 3,
        DataScope.Location => 4,
        DataScope.Company => 5,
        _ => 0,
    };

    public static DataScope Widest(DataScope left, DataScope right) => Rank(right) > Rank(left) ? right : left;
}

/// <summary>Modüllerin kataloğa katkı verdiği izin tanımı. Key biçimi: {modül}.{kaynak}.{eylem}.</summary>
public sealed record Permission(string Key, string Module, string DisplayName, string Group, string? Description = null)
{
    public override string ToString() => Key;
}

/// <summary>Her modül kendi izinlerini bu arayüzle sunar; Identity modülü birleşik kataloğu döner.</summary>
public interface IPermissionProvider
{
    IEnumerable<Permission> Permissions { get; }
}

/// <summary>Kullanıcının etkin izinlerini ve kapsamlarını çözer (Redis cache'li).</summary>
public interface IPermissionService
{
    Task<bool> HasAsync(Guid userId, string permission, CancellationToken cancellationToken = default);

    Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<DataScope> GetScopeAsync(Guid userId, string permission, CancellationToken cancellationToken = default);
}

/// <summary>"İzin var, ama kimin üzerinde?" — görünür çalışan kümesi. null = filtre yok (Company).</summary>
public interface IDataScopeService
{
    Task<IReadOnlySet<Guid>?> GetVisibleEmployeeIdsAsync(string permission, CancellationToken cancellationToken = default);

    Task<bool> CanAccessEmployeeAsync(string permission, Guid employeeId, CancellationToken cancellationToken = default);
}

/// <summary>Handler üzerinde gerekli izni bildirir; AuthorizationBehaviour denetler.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class RequiresPermissionAttribute(string permission) : Attribute
{
    public string Permission { get; } = permission;
}

/// <summary>Uç noktanın/handler'ın hangi modül aboneliğini gerektirdiğini bildirir (Billing entitlement).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequiresModuleAttribute(string moduleCode) : Attribute
{
    public string ModuleCode { get; } = moduleCode;
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.UnitAuthorizations;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Caching;
using Sense.Shared.Infrastructure.Messaging.Behaviours;

namespace Sense.Modules.Identity.Infrastructure.Security;

/// <summary>RoleScope: rol/override kaynaklı kapsam (izin rolden gelmiyorsa null); UnitIds: birim yetkisi kök birimleri.</summary>
public sealed record PermissionScopeDetail(DataScope? RoleScope, IReadOnlyList<Guid> UnitIds)
{
    public static readonly PermissionScopeDetail None = new(null, []);
}

/// <summary>
/// Veri kapsamı çözümü: rol kapsamı (Self/Team/Department/Location/Company; hiyerarşi IEmployeeScopeLookup ile) ∪ birim yetkileri
/// (yetkili birim + aktif alt birimlerindeki aktif çalışanlar). Platform kimliği yalnız aktif impersonation oturumuyla sınırsızdır;
/// gerekli kontratlar kayıtlı değilse güvenli tarafta (dar kapsam) kalınır.
/// </summary>
public sealed class DataScopeService(
    PermissionService permissions,
    ICurrentUser user,
    ITenantContext tenant,
    IServiceProvider services,
    IImpersonationSessionValidator? impersonationSessions = null,
    IEmployeeScopeLookup? scopeLookup = null,
    IOrgUnitLookup? orgUnits = null,
    IEmployeeLookup? employeeLookup = null) : IDataScopeService
{
    private static readonly IReadOnlySet<Guid> Nothing = new HashSet<Guid>();

    public async Task<IReadOnlySet<Guid>?> GetVisibleEmployeeIdsAsync(string permission, CancellationToken cancellationToken = default)
    {
        var platform = await ResolvePlatformAsync(cancellationToken).ConfigureAwait(false);
        if (platform is not null)
        {
            return platform.Value ? null : Nothing;
        }

        if (user.UserId is not { } userId)
        {
            return new HashSet<Guid>();
        }

        var detail = await permissions.GetScopeDetailAsync(userId, permission, cancellationToken).ConfigureAwait(false);
        if (detail.RoleScope == DataScope.Company)
        {
            return null;
        }

        var visible = user.EmployeeId is { } eid ? new HashSet<Guid> { eid } : new HashSet<Guid>();
        if (detail.RoleScope is DataScope.Team or DataScope.Department or DataScope.Location && scopeLookup is not null && user.EmployeeId is { } self)
        {
            visible.UnionWith(await scopeLookup.GetEmployeeIdsInScopeAsync(self, detail.RoleScope.Value, cancellationToken).ConfigureAwait(false));
        }

        // Resolved lazily: Employee's directory itself depends on IDataScopeService (selection resolution).
        if (detail.UnitIds.Count > 0 && orgUnits is not null && tenant.IsResolved && services.GetService(typeof(IEmployeeDirectory)) is IEmployeeDirectory employeeDirectory)
        {
            var units = await orgUnits.GetSelfAndDescendantIdsAsync(tenant.TenantId, detail.UnitIds.ToList(), cancellationToken).ConfigureAwait(false);
            visible.UnionWith(await employeeDirectory.ListActiveEmployeeIdsInOrgUnitsAsync(tenant.TenantId, units, cancellationToken).ConfigureAwait(false));
        }

        return visible;
    }

    public async Task<bool> CanAccessEmployeeAsync(string permission, Guid employeeId, CancellationToken cancellationToken = default)
    {
        var visible = await GetVisibleEmployeeIdsAsync(permission, cancellationToken).ConfigureAwait(false);
        return visible is null || visible.Contains(employeeId);
    }

    public async Task<IReadOnlySet<Guid>?> GetAccessibleOrgUnitIdsAsync(string permission, CancellationToken cancellationToken = default)
    {
        var platform = await ResolvePlatformAsync(cancellationToken).ConfigureAwait(false);
        if (platform is not null)
        {
            return platform.Value ? null : Nothing;
        }

        if (user.UserId is not { } userId || !tenant.IsResolved)
        {
            return new HashSet<Guid>();
        }

        var detail = await permissions.GetScopeDetailAsync(userId, permission, cancellationToken).ConfigureAwait(false);
        if (detail.RoleScope == DataScope.Company)
        {
            return null;
        }

        var roots = new List<Guid>(detail.UnitIds);
        if (detail.RoleScope == DataScope.Department && employeeLookup is not null && user.EmployeeId is { } self
            && await employeeLookup.GetOrgUnitIdAsync(tenant.TenantId, self, cancellationToken).ConfigureAwait(false) is { } ownUnit)
        {
            roots.Add(ownUnit);
        }

        if (roots.Count == 0 || orgUnits is null)
        {
            return new HashSet<Guid>();
        }

        return (await orgUnits.GetSelfAndDescendantIdsAsync(tenant.TenantId, roots.Distinct().ToList(), cancellationToken).ConfigureAwait(false)).ToHashSet();
    }

    /// <summary>null: platform kimliği değil (normal kural); true: sınırsız (sistem bağlamı veya aktif impersonation); false: erişim yok.</summary>
    private async Task<bool?> ResolvePlatformAsync(CancellationToken ct)
    {
        if (user.IsSystemContext())
        {
            return true;
        }

        if (!user.ClaimsPlatformAccess())
        {
            return null;
        }

        return impersonationSessions is not null && await impersonationSessions.IsActiveAsync(ct).ConfigureAwait(false);
    }
}

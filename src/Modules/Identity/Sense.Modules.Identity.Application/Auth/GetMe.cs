using Sense.Modules.Identity.Application.Tenants;
using Sense.Modules.Identity.Domain;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Identity.Application.Auth;

/// <summary>Oturum açmış kullanıcının profili, kiracısı, rolleri, etkin izinleri ve kapsamları (ön yüz menü/guard'ları için).</summary>
public sealed record GetMeQuery : IQuery<MeDto>;

public sealed class GetMeHandler(
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IUserRepository users,
    ITenantRepository tenants,
    IRoleRepository roles,
    IPermissionService permissions,
    IImpersonationSessionValidator? impersonationSessions = null) : IQueryHandler<GetMeQuery, MeDto>
{
    public async Task<Result<MeDto>> Handle(GetMeQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null || !tenantContext.IsResolved)
        {
            return Error.Unauthorized(Shared.Kernel.Results.ErrorCodes.Unauthenticated);
        }

        var tenant = await tenants.GetByIdAsync(tenantContext.TenantId, cancellationToken).ConfigureAwait(false);
        if (tenant is null)
        {
            return Error.Unauthorized(Shared.Kernel.Results.ErrorCodes.Unauthenticated);
        }

        // Impersonation (faz0-durum-takibi.md #11): the JWT's `sub` is the platform user's id, a row that only
        // exists in the PLATFORM tenant. Looking it up under the impersonated tenant's query filter (the normal
        // path below) always returns null and used to 401 here — the only reason a platform console could never
        // actually load the app shell. AuthorizationBehaviour already bypasses every [RequiresPermission] check
        // for the duration of an active session, so this branch mirrors that instead of resolving this tenant's
        // roles/permissions for a user row that was never in it to begin with.
        if (currentUser.ImpersonatorUserId is not null
            && impersonationSessions is not null
            && await impersonationSessions.IsActiveAsync(cancellationToken).ConfigureAwait(false))
        {
            var platformUser = await users.GetByIdAcrossTenantsAsync(currentUser.UserId.Value, cancellationToken).ConfigureAwait(false);
            if (platformUser is null)
            {
                return Error.Unauthorized(Shared.Kernel.Results.ErrorCodes.Unauthenticated);
            }

            return new MeDto(
                platformUser.Id, platformUser.Email, platformUser.FirstName, platformUser.LastName, EmployeeId: null,
                platformUser.PreferredLanguage, platformUser.MfaEnabled,
                new TenantDto(tenant.Id, tenant.Slug, tenant.Name, tenant.TaxNumber, tenant.Status.ToString(), tenant.TrialEndsAt, tenant.Locale, tenant.TimeZone),
                Roles: [],
                Permissions: [],
                Scopes: new Dictionary<string, DataScope>(StringComparer.Ordinal),
                new FeaturesDto(AccessSettingsDto.AllEnabled, AccessSettingsBypass: true));
        }

        var user = await users.GetByIdAsync(currentUser.UserId.Value, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Error.Unauthorized(Shared.Kernel.Results.ErrorCodes.Unauthenticated);
        }

        var userRoles = await roles.GetByIdsAsync(user.Roles.Select(r => r.RoleId), cancellationToken).ConfigureAwait(false);
        var effective = await permissions.GetPermissionsAsync(user.Id, cancellationToken).ConfigureAwait(false);

        var bypass = effective.Contains(IdentityPermissions.AccessSettingsBypass);

        var scopes = new Dictionary<string, DataScope>(StringComparer.Ordinal);
        foreach (var p in effective)
        {
            scopes[p] = await permissions.GetScopeAsync(user.Id, p, cancellationToken).ConfigureAwait(false);
        }

        return new MeDto(
            user.Id, user.Email, user.FirstName, user.LastName, user.EmployeeId, user.PreferredLanguage, user.MfaEnabled,
            new TenantDto(tenant.Id, tenant.Slug, tenant.Name, tenant.TaxNumber, tenant.Status.ToString(), tenant.TrialEndsAt, tenant.Locale, tenant.TimeZone),
            userRoles.Select(r => new RoleRefDto(r.Id, r.Code, r.Name)).ToList(),
            effective.Order(StringComparer.Ordinal).ToList(),
            scopes,
            new FeaturesDto(bypass ? AccessSettingsDto.AllEnabled : AccessSettingsDto.From(tenant.AccessSettings), bypass));
    }
}

using Hangfire.Dashboard;
using Sense.Shared.Contracts.Security;
using Sense.Modules.Identity.Domain.Roles;

namespace Sense.Api.Authorization;

/// <summary>Hangfire's interactive dashboard can mutate jobs, so limit it to direct platform SuperAdmin sessions.</summary>
public sealed class PlatformDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var principal = context.GetHttpContext().User;
        return principal.Identity?.IsAuthenticated == true
            && principal.HasClaim(ClaimNames.TenantSlug, "platform")
            && !principal.HasClaim(claim => claim.Type == ClaimNames.Impersonator || claim.Type == ClaimNames.ApiKey)
            && principal.HasClaim(ClaimNames.Roles, SystemRoleCodes.SuperAdmin);
    }
}

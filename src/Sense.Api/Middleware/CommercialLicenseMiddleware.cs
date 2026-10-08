using Sense.Modules.Identity.Application.Tenants;
using Sense.Modules.Identity.Domain;
using Sense.Shared.Contracts.Context;

namespace Sense.Api.Middleware;

/// <summary>Rechecks entitlement on every authenticated request, including already issued access tokens.</summary>
public sealed class CommercialLicenseMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, ITenantContext context, ITenantRepository tenants, TimeProvider clock)
    {
        if (http.User.Identity?.IsAuthenticated == true && context.IsResolved)
        {
            var tenant = await tenants.GetByIdAsync(context.TenantId, http.RequestAborted).ConfigureAwait(false);
            if (tenant is not null && !CommercialLicenseRules.CanAccess(tenant, clock.GetUtcNow().UtcDateTime))
            {
                http.Response.StatusCode = StatusCodes.Status403Forbidden;
                await http.Response.WriteAsJsonAsync(new { code = "commercial.license.expired_or_revoked", message = "Demo veya lisans erişiminiz sona erdi. Lisansınızı yenilemek için satış ekibiyle iletişime geçin." }, http.RequestAborted).ConfigureAwait(false);
                return;
            }
        }
        await next(http).ConfigureAwait(false);
    }
}

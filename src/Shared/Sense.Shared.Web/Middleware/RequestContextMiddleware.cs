using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Context;
using Sense.Shared.Infrastructure.Observability;

namespace Sense.Shared.Web.Middleware;

/// <summary>
/// Kimlik doğrulamasından sonra çalışır: JWT claim'lerinden ICurrentUser'ı doldurur ve tenant claim'i ile kiracı bağlamını kurar.
/// Sıra: UseCorrelationId → UseAuthentication → UseRequestContext → UseAuthorization. URL'de kiracı asla taşınmaz.
/// Correlation id, pipeline'ın başında CorrelationIdMiddleware tarafından zaten üretilip HttpContext.Items'a
/// yazılmıştır (yoksa TraceIdentifier'a geri düşülür: örn. CorrelationIdMiddleware eklenmeden çalışan testler).
/// </summary>
public sealed class RequestContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, CurrentUserAccessor userAccessor, ITenantContextSetter tenantSetter, IAuditSourceAccessor auditSource)
    {
        var correlationId = context.Items[CorrelationIdContext.HttpContextItemKey] as string ?? context.TraceIdentifier;

        var principal = context.User;
        IDisposable? tenantScope = null;
        IDisposable? sourceScope = null;

        if (principal.Identity?.IsAuthenticated == true)
        {
            var roles = principal.FindAll(ClaimNames.Roles).Select(c => c.Value).ToHashSet(StringComparer.Ordinal);
            var apiKeyId = ParseGuid(principal.FindFirst(ClaimNames.ApiKey)?.Value);

            var user = new AuthenticatedUser(
                UserId: ParseGuid(principal.FindFirst(ClaimNames.Subject)?.Value) ?? Guid.Empty,
                EmployeeId: ParseGuid(principal.FindFirst(ClaimNames.Employee)?.Value),
                Email: principal.FindFirst(ClaimNames.Email)?.Value,
                DisplayName: principal.FindFirst(ClaimNames.Name)?.Value,
                Roles: roles,
                IsPlatformAdmin: principal.HasClaim(c => c.Type == ClaimNames.PlatformAdmin && c.Value == ClaimNames.TrueValue),
                ImpersonatorUserId: ParseGuid(principal.FindFirst(ClaimNames.Impersonator)?.Value),
                ApiKeyId: apiKeyId,
                CorrelationId: correlationId,
                IpAddress: context.Connection.RemoteIpAddress?.ToString());

            userAccessor.Set(user);

            if (ParseGuid(principal.FindFirst(ClaimNames.Tenant)?.Value) is { } tid)
            {
                tenantScope = tenantSetter.BeginScope(tid, principal.FindFirst(ClaimNames.TenantSlug)?.Value);
            }

            // senseik-018 (E-01): audit log Source - an API key claim means the call came in through the API
            // integration surface rather than a browser session, even though both are otherwise indistinguishable
            // "authenticated user" requests to IdentityAuditLogInterceptor.
            sourceScope = auditSource.BeginScope(apiKeyId is not null ? AuditSources.Api : AuditSources.Web);
        }

        try
        {
            await next(context).ConfigureAwait(false);
        }
        finally
        {
            sourceScope?.Dispose();
            tenantScope?.Dispose();
        }
    }

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var g) ? g : null;
}

public static class RequestContextMiddlewareExtensions
{
    public static IApplicationBuilder UseRequestContext(this IApplicationBuilder app) => app.UseMiddleware<RequestContextMiddleware>();
}

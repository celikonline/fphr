using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Context;

namespace Sense.Shared.Web.Middleware;

/// <summary>
/// Kimlik doğrulamasından sonra çalışır: JWT claim'lerinden ICurrentUser'ı doldurur ve tenant claim'i ile kiracı bağlamını kurar.
/// Sıra: UseAuthentication → UseRequestContext → UseAuthorization. URL'de kiracı asla taşınmaz.
/// </summary>
public sealed class RequestContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, CurrentUserAccessor userAccessor, ITenantContextSetter tenantSetter)
    {
        var correlationId = context.Request.Headers[SenseHeaderNames.CorrelationId].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = context.TraceIdentifier;
        }

        context.Response.Headers[SenseHeaderNames.CorrelationId] = correlationId;

        var principal = context.User;
        IDisposable? tenantScope = null;

        if (principal.Identity?.IsAuthenticated == true)
        {
            var roles = principal.FindAll(ClaimNames.Roles).Select(c => c.Value).ToHashSet(StringComparer.Ordinal);

            var user = new AuthenticatedUser(
                UserId: ParseGuid(principal.FindFirst(ClaimNames.Subject)?.Value) ?? Guid.Empty,
                EmployeeId: ParseGuid(principal.FindFirst(ClaimNames.Employee)?.Value),
                Email: principal.FindFirst(ClaimNames.Email)?.Value,
                DisplayName: principal.FindFirst(ClaimNames.Name)?.Value,
                Roles: roles,
                IsPlatformAdmin: principal.HasClaim(c => c.Type == ClaimNames.PlatformAdmin && c.Value == ClaimNames.TrueValue),
                ImpersonatorUserId: ParseGuid(principal.FindFirst(ClaimNames.Impersonator)?.Value),
                ApiKeyId: ParseGuid(principal.FindFirst(ClaimNames.ApiKey)?.Value),
                CorrelationId: correlationId,
                IpAddress: context.Connection.RemoteIpAddress?.ToString());

            userAccessor.Set(user);

            if (ParseGuid(principal.FindFirst(ClaimNames.Tenant)?.Value) is { } tid)
            {
                tenantScope = tenantSetter.BeginScope(tid, principal.FindFirst(ClaimNames.TenantSlug)?.Value);
            }
        }

        try
        {
            await next(context).ConfigureAwait(false);
        }
        finally
        {
            tenantScope?.Dispose();
        }
    }

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var g) ? g : null;
}

public static class RequestContextMiddlewareExtensions
{
    public static IApplicationBuilder UseRequestContext(this IApplicationBuilder app) => app.UseMiddleware<RequestContextMiddleware>();
}

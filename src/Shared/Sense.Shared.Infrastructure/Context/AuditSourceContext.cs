using Sense.Shared.Contracts.Context;

namespace Sense.Shared.Infrastructure.Context;

/// <summary>
/// AsyncLocal-backed <see cref="IAuditSourceAccessor"/>, the same pattern as <see cref="TenantContext"/>:
/// RequestContextMiddleware sets Web/Api for the lifetime of an authenticated HTTP request, RunImportJobHandler
/// sets Import around a bulk import run. Nothing sets it for background/system work (DataSeeder, OutboxProcessor,
/// InProcessEventBus, EmailDeliveryProcessor, SystemRolePermissionSynchronizer) - those already switch the actor
/// with <see cref="CurrentUserAccessor.UseSystem"/>, and IdentityAuditLogInterceptor falls back to System from
/// that actor when <see cref="Current"/> is null.
/// </summary>
public sealed class AuditSourceContext : IAuditSourceAccessor
{
    private static readonly AsyncLocal<string?> Scope = new();

    public string? Current => Scope.Value;

    public IDisposable BeginScope(string source)
    {
        var previous = Scope.Value;
        Scope.Value = source;
        return new Restorer(previous);
    }

    private sealed class Restorer(string? previous) : IDisposable
    {
        public void Dispose() => Scope.Value = previous;
    }
}

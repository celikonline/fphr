using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Notifications;
using Sense.Shared.Contracts.Security;

namespace Sense.Shared.Infrastructure.Notifications;

/// <summary>
/// Real-time notification hub für tenant-scoped events.
/// Clients automatically join tenant group on connection.
/// </summary>
/// <remarks>
/// Lives in Sense.Shared.Infrastructure (not Sense.Api, where it originally lived) because
/// SignalRNotificationService needs IHubContext&lt;NotificationsHub&gt; and Infrastructure
/// cannot reference Sense.Api (the dependency runs the other way).
/// </remarks>
public class NotificationsHub : Hub
{
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<NotificationsHub> _logger;

    public NotificationsHub(ITenantContext tenantContext, ILogger<NotificationsHub> logger)
    {
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Called when a client connects. Extracts tenantId from JWT claims and adds connection to tenant group.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        try
        {
            var tenantId = ExtractTenantIdFromContext();
            if (tenantId == Guid.Empty)
            {
                _logger.LogWarning("Connection attempt without valid tenant context: {ConnectionId}", Context.ConnectionId);
                await base.OnDisconnectedAsync(new InvalidOperationException("Tenant ID is required"));
                return;
            }

            // Auto-join tenant group
            var groupName = SignalRGroupNames.ForTenant(tenantId);
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

            // Auto-join the per-user group (user-targeted notifications, e.g. Notifications module in-app items)
            if (Guid.TryParse(Context.User?.FindFirst(ClaimNames.Subject)?.Value, out var userId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, SignalRGroupNames.ForUser(userId));
            }

            _logger.LogInformation(
                "Client connected to tenant group. ConnectionId: {ConnectionId}, TenantId: {TenantId}",
                Context.ConnectionId,
                tenantId);

            await base.OnConnectedAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in OnConnectedAsync");
            throw;
        }
    }

    /// <summary>
    /// Called when a client disconnects.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception != null)
        {
            _logger.LogWarning(exception, "Client disconnected with exception: {ConnectionId}", Context.ConnectionId);
        }
        else
        {
            _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Extracts tenantId from JWT claims in the HTTP context.
    /// </summary>
    /// <remarks>
    /// The tenant claim should be added during JWT generation.
    /// Falls back to TenantContext if available.
    /// </remarks>
    private Guid ExtractTenantIdFromContext()
    {
        // First, try to get from claims (JWT)
        var tenantClaim = Context.User?.FindFirst(ClaimNames.Tenant);
        if (tenantClaim != null && Guid.TryParse(tenantClaim.Value, out var tenantId))
        {
            return tenantId;
        }

        // Fallback to TenantContext if already set (shouldn't happen in Hub context)
        if (_tenantContext.IsResolved)
        {
            return _tenantContext.TenantId;
        }

        return Guid.Empty;
    }
}

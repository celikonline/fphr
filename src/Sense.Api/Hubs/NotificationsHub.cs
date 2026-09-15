using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Sense.Shared.Contracts.Context;

namespace Sense.Api.Hubs;

/// <summary>
/// Real-time notification hub für tenant-scoped events.
/// Clients automatically join tenant group on connection.
/// </summary>
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
            var groupName = GetTenantGroupName(tenantId);
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

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
        var tenantClaim = Context.User?.FindFirst("tenant");
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

    /// <summary>
    /// Gets the tenant group name for SignalR group isolation.
    /// </summary>
    public static string GetTenantGroupName(Guid tenantId) => $"tenant-{tenantId:N}";
}

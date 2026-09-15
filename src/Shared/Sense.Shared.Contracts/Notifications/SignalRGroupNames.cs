namespace Sense.Shared.Contracts.Notifications;

/// <summary>
/// Helper class for SignalR group naming conventions.
/// Ensures consistent group names across the application.
/// </summary>
public static class SignalRGroupNames
{
    /// <summary>
    /// Gets the SignalR group name for a tenant.
    /// Format: "tenant-{tenantId:N}" (no dashes in GUID)
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <returns>The group name for this tenant.</returns>
    public static string ForTenant(Guid tenantId) => $"tenant-{tenantId:N}";

    /// <summary>
    /// Gets the SignalR group name for a single user (every connection of that user joins it).
    /// Format: "user-{userId:N}". Used instead of Clients.User because the JWT keeps "sub" unmapped
    /// (MapInboundClaims = false), which the default IUserIdProvider does not read.
    /// </summary>
    public static string ForUser(Guid userId) => $"user-{userId:N}";
}

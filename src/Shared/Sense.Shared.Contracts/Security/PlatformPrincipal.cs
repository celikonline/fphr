using Sense.Shared.Contracts.Context;

namespace Sense.Shared.Contracts.Security;

/// <summary>Classifies platform-flagged principals so every authorization point applies the same fail-closed rule.</summary>
public static class PlatformPrincipal
{
    /// <summary>In-process system context (background jobs): platform flag without a user id. Never produced from a JWT (the middleware always sets a user id).</summary>
    public static bool IsSystemContext(this ICurrentUser user) =>
        user.IsAuthenticated && user.IsPlatformAdmin && user.UserId is null && user.ImpersonatorUserId is null;

    /// <summary>A request principal claiming platform access (platform_admin / act claim); it may act on the tenant only while <see cref="IImpersonationSessionValidator.IsActiveAsync"/> is true.</summary>
    public static bool ClaimsPlatformAccess(this ICurrentUser user) =>
        user.IsAuthenticated && user.UserId is not null && (user.IsPlatformAdmin || user.ImpersonatorUserId is not null);
}

using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Notifications.Domain.PushDevices;

/// <summary>Expo push platforms a device token may be registered for.</summary>
public static class PushPlatforms
{
    public const string Ios = "ios";
    public const string Android = "android";

    public static IReadOnlyList<string> All { get; } = [Ios, Android];
}

/// <summary>
/// A device's Expo push token for one user. A token belongs to a physical device, not a user: if the same
/// device logs out and a different employee logs in, the old row must be revoked (see Revoke) before the
/// new user's row is inserted, otherwise both accounts would receive pushes on that device. Only one row per
/// token may be active (RevokedAt is null) at a time - enforced by a partial unique index on Token.
/// </summary>
public sealed class PushDeviceToken : TenantEntity<Guid>
{
    private PushDeviceToken()
    {
    }

    public PushDeviceToken(Guid tenantId, Guid userId, string token, string platform, string? deviceName, DateTime now)
        : base(Guid.CreateVersion7(), tenantId)
    {
        UserId = userId;
        Token = Guard.MaxLength(Guard.NotEmpty(token), NotificationsLimits.PushTokenMaxLength);
        Platform = Guard.MaxLength(Guard.NotEmpty(platform), NotificationsLimits.PushPlatformMaxLength);
        DeviceName = string.IsNullOrWhiteSpace(deviceName) ? null : Guard.MaxLength(deviceName, NotificationsLimits.DeviceNameMaxLength);
        LastSeenAt = now;
    }

    public Guid UserId { get; private set; }

    public string Token { get; private set; } = string.Empty;

    public string Platform { get; private set; } = string.Empty;

    public string? DeviceName { get; private set; }

    public DateTime LastSeenAt { get; private set; }

    public DateTime? RevokedAt { get; private set; }

    public bool IsActive => RevokedAt is null;

    /// <summary>Idempotent re-registration (app foreground, token unchanged).</summary>
    public void Touch(DateTime now) => LastSeenAt = now;

    /// <summary>Logout, or Expo reported DeviceNotRegistered for this token.</summary>
    public void Revoke(DateTime now) => RevokedAt ??= now;
}

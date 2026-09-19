namespace Sense.Modules.Documents.Domain.SharedFiles;

/// <summary>
/// Tenant's shared-file storage quota usage snapshot (senseik-028 acceptance: "Kullanılan Alan %22 (22.88 MB / 104.86 MB)").
/// A pure value type so the percentage math is unit-testable without a database round trip.
/// </summary>
public sealed record SharedFileQuota(long UsedBytes, long QuotaBytes, double UsedPercent, bool IsExceeded)
{
    /// <summary>Computes usage percentage (0-100, clamped) and whether the tenant is at/over quota.
    /// A zero-byte quota is treated as always exceeded once anything is used, and never divides by zero.</summary>
    public static SharedFileQuota Calculate(long usedBytes, long quotaBytes)
    {
        if (quotaBytes <= 0)
        {
            return new SharedFileQuota(usedBytes, quotaBytes, usedBytes > 0 ? 100 : 0, usedBytes > 0);
        }

        var percent = Math.Clamp(usedBytes * 100.0 / quotaBytes, 0, 100);
        return new SharedFileQuota(usedBytes, quotaBytes, percent, usedBytes >= quotaBytes);
    }

    /// <summary>True when adding <paramref name="additionalBytes"/> to the current usage would exceed the quota.</summary>
    public bool WouldExceed(long additionalBytes) => UsedBytes + additionalBytes > QuotaBytes;
}

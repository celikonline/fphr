using Sense.Modules.Attendance.Application.SelfCheckIns;

namespace Sense.Modules.Attendance.Tests;

/// <summary>Deterministic <see cref="ITenantLocalClock"/> for handler tests: the local wall-clock is supplied directly (defaults to the UTC value).</summary>
internal sealed class FixedLocalClock(DateTime utc, DateTime? local = null) : ITenantLocalClock
{
    public Task<TenantLocalNow> NowAsync(Guid tenantId, CancellationToken cancellationToken) =>
        Task.FromResult(new TenantLocalNow(utc, local ?? utc));
}

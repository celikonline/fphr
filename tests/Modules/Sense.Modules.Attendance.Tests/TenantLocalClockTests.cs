using NSubstitute;
using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Modules.Identity.Contracts;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

/// <summary>Tenant-local "now" used to stamp self-servis check-ins (S-31): punches must be wall-clock in the tenant zone, not UTC.</summary>
public sealed class TenantLocalClockTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset UtcNow = new(2026, 9, 21, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task NowAsync_TenantInIstanbul_ReturnsUtcPlusThree()
    {
        var zones = Substitute.For<ITenantTimeZoneLookup>();
        zones.FindTimeZoneIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns("Europe/Istanbul");

        var now = await new TenantLocalClock(zones, new FixedTimeProvider(UtcNow)).NowAsync(TenantId, TestContext.Current.CancellationToken);

        now.Utc.ShouldBe(new DateTime(2026, 9, 21, 6, 0, 0, DateTimeKind.Utc));
        now.Local.ShouldBe(new DateTime(2026, 9, 21, 9, 0, 0));
    }

    [Fact]
    public async Task NowAsync_UnknownTenantZone_FallsBackToUtc()
    {
        var zones = Substitute.For<ITenantTimeZoneLookup>();
        zones.FindTimeZoneIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns((string?)null);

        var now = await new TenantLocalClock(zones, new FixedTimeProvider(UtcNow)).NowAsync(TenantId, TestContext.Current.CancellationToken);

        now.Local.ShouldBe(new DateTime(2026, 9, 21, 6, 0, 0));
    }

    [Fact]
    public async Task NowAsync_UnresolvableZoneId_FallsBackToUtc()
    {
        var zones = Substitute.For<ITenantTimeZoneLookup>();
        zones.FindTimeZoneIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns("Not/AZone");

        var now = await new TenantLocalClock(zones, new FixedTimeProvider(UtcNow)).NowAsync(TenantId, TestContext.Current.CancellationToken);

        now.Local.ShouldBe(new DateTime(2026, 9, 21, 6, 0, 0));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

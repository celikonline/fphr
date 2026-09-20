using NSubstitute;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Leave.Application.Requests;
using Shouldly;
using Xunit;

namespace Sense.Modules.Leave.Tests;

/// <summary>
/// Ürün kararı (denetim A10): self-servis izin talebinin başlangıcı, kiracının yerel bugününden en fazla
/// <see cref="LeaveBackdatePolicy.SelfServiceMaxBackdateDays"/> gün geriye gidebilir.
/// </summary>
public sealed class LeaveBackdatePolicyTests
{
    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private static LeaveBackdatePolicy PolicyAt(DateTimeOffset utcNow, string? zoneId = null)
    {
        var zones = Substitute.For<ITenantTimeZoneLookup>();
        zones.FindTimeZoneIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(zoneId);
        return new LeaveBackdatePolicy(new FixedTimeProvider(utcNow), zones);
    }

    [Fact]
    public void DefaultLimit_IsSevenDays() => LeaveBackdatePolicy.SelfServiceMaxBackdateDays.ShouldBe(7);

    [Fact]
    public async Task Earliest_IsTodayMinusTheLimit_WithoutTenantZone()
    {
        var policy = PolicyAt(new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.Zero));

        (await policy.EarliestSelfServiceStartAsync(Guid.NewGuid(), CancellationToken.None)).ShouldBe(new DateOnly(2026, 9, 13));
    }

    [Fact]
    public async Task Earliest_UsesTheTenantLocalDate_NotUtc()
    {
        // 2026-09-20 22:30 UTC is already 2026-09-21 01:30 in Istanbul (UTC+3).
        var policy = PolicyAt(new DateTimeOffset(2026, 9, 20, 22, 30, 0, TimeSpan.Zero), "Europe/Istanbul");

        (await policy.EarliestSelfServiceStartAsync(Guid.NewGuid(), CancellationToken.None)).ShouldBe(new DateOnly(2026, 9, 14));
    }

    [Fact]
    public async Task Earliest_FallsBackToUtc_WhenTheZoneIsUnknown()
    {
        var policy = PolicyAt(new DateTimeOffset(2026, 9, 20, 22, 30, 0, TimeSpan.Zero), "Not/AZone");

        (await policy.EarliestSelfServiceStartAsync(Guid.NewGuid(), CancellationToken.None)).ShouldBe(new DateOnly(2026, 9, 13));
    }

    [Fact]
    public async Task Earliest_WorksWithoutATimeZoneLookup()
    {
        var policy = new LeaveBackdatePolicy(new FixedTimeProvider(new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero)));

        (await policy.EarliestSelfServiceStartAsync(Guid.NewGuid(), CancellationToken.None)).ShouldBe(new DateOnly(2025, 12, 27));
    }
}

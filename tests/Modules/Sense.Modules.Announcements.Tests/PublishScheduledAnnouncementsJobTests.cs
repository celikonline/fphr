using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Announcements.Application;
using Sense.Modules.Announcements.Domain;
using Sense.Modules.Announcements.Infrastructure.Jobs;
using Xunit;

namespace Sense.Modules.Announcements.Tests;

/// <summary>
/// <see cref="PublishScheduledAnnouncementsJob"/> (TK-016): the DI/tenant-scope loop (RunAsync) needs a real
/// AnnouncementsDbContext and is exercised only by manual/integration testing (same coverage shape as Leave's
/// LeaveBalanceRecalculationJob, whose tenant-enumeration loop is Postgres-Testcontainers-only). What this suite
/// verifies at the unit level is the actual scheduling decision (<see cref="PublishScheduledAnnouncementsJob.PublishDueAsync"/>):
/// it must use the injected <see cref="TimeProvider"/> ("now"), not <see cref="DateTime.UtcNow"/> or a real clock/sleep,
/// so tests can fix "now" and assert deterministically (kabul kriteri #4).
/// </summary>
public sealed class PublishScheduledAnnouncementsJobTests
{
    /// <summary>Test-only fake clock (mirrors <c>Sense.Modules.Documents.Tests.DocumentExpiryReminderJobHandlerTests.FixedTimeProvider</c> -
    /// no FakeTimeProvider package reference in this test project).</summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static PublishScheduledAnnouncementsJob CreateJob(DateTimeOffset now) =>
        new(Substitute.For<IServiceScopeFactory>(), new FixedTimeProvider(now), NullLogger<PublishScheduledAnnouncementsJob>.Instance);

    [Fact]
    public async Task PublishDueAsync_PublishesOnlyAnnouncementsReturnedAsDue_UsingInjectedClock()
    {
        var tenantId = Guid.NewGuid();
        var fixedNow = new DateTimeOffset(2026, 9, 25, 9, 0, 0, TimeSpan.Zero);
        var due = Announcement.Create(
            tenantId, "Yemekhane tatili", "İçerik", AnnouncementType.General, AnnouncementPriority.Normal,
            scheduledFor: fixedNow.UtcDateTime.AddMinutes(-1)).Value;

        var announcements = Substitute.For<IAnnouncementRepository>();
        announcements.GetDueScheduledAsync(tenantId, fixedNow.UtcDateTime, Arg.Any<CancellationToken>()).Returns([due]);
        var unitOfWork = Substitute.For<IAnnouncementsUnitOfWork>();

        var job = CreateJob(fixedNow);
        var count = await job.PublishDueAsync(announcements, unitOfWork, tenantId, CancellationToken.None);

        Assert.Equal(1, count);
        Assert.Equal(AnnouncementStatus.Published, due.Status);
        Assert.Equal(fixedNow.UtcDateTime, due.PublishedAt);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishDueAsync_NoDueAnnouncements_DoesNotSave()
    {
        var tenantId = Guid.NewGuid();
        var fixedNow = new DateTimeOffset(2026, 9, 25, 9, 0, 0, TimeSpan.Zero);

        var announcements = Substitute.For<IAnnouncementRepository>();
        announcements.GetDueScheduledAsync(tenantId, fixedNow.UtcDateTime, Arg.Any<CancellationToken>()).Returns([]);
        var unitOfWork = Substitute.For<IAnnouncementsUnitOfWork>();

        var job = CreateJob(fixedNow);
        var count = await job.PublishDueAsync(announcements, unitOfWork, tenantId, CancellationToken.None);

        Assert.Equal(0, count);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}

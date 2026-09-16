using Sense.Shared.Contracts.BackgroundJobs;
using Sense.Shared.Infrastructure.BackgroundJobs;
using Shouldly;
using Xunit;

namespace Sense.Shared.Infrastructure.Tests.BackgroundJobs;

/// <summary>Faz 1 tur 1: per-tenant seed entry for <see cref="JobType.DocumentExpiryReminder"/>.</summary>
public sealed class BackgroundJobSeedingTests
{
    [Fact]
    public void GetDefaultSeeds_ContainsExactlyOneDocumentExpiryReminderEntry()
    {
        var seeds = BackgroundJobSeeding.GetDefaultSeeds().Where(s => s.JobType == JobType.DocumentExpiryReminder).ToList();

        seeds.Count.ShouldBe(1);
        seeds[0].CronExpression.ShouldBe("0 6 * * *");
    }
}

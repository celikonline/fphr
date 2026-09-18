using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Sense.Shared.Contracts.BackgroundJobs;
using Sense.Shared.Infrastructure.BackgroundJobs;
using Shouldly;
using Xunit;

namespace Sense.Shared.Infrastructure.Tests.BackgroundJobs;

/// <summary>G-1-13: <see cref="BackgroundJobSeeding.GetDefaultSeeds"/> hiçbir yerden çağrılmıyordu (KVKK md.7
/// açığı) - <see cref="BackgroundJobSeedSynchronizer"/> her yazılabilir kiracı için bunu <see cref="IBackgroundJobService.RecurringAsync"/>
/// ile kaydeder/günceller, idempotent olmalıdır.</summary>
public sealed class BackgroundJobSeedSynchronizerTests
{
    [Fact]
    public async Task SyncAllTenantsAsync_RegistersEverySeedForEveryActiveTenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var tenants = Substitute.For<IActiveTenantDirectory>();
        tenants.GetActiveTenantIdsAsync(Arg.Any<CancellationToken>()).Returns(new List<Guid> { tenantA, tenantB });
        var jobs = new RecordingBackgroundJobService();
        var synchronizer = CreateSynchronizer(tenants, jobs);
        var seeds = BackgroundJobSeeding.GetDefaultSeeds().ToList();

        var registered = await synchronizer.SyncAllTenantsAsync(CancellationToken.None);

        registered.ShouldBe(2 * seeds.Count);
        jobs.Calls.Count.ShouldBe(2 * seeds.Count);
        foreach (var tenantId in new[] { tenantA, tenantB })
        {
            foreach (var seed in seeds)
            {
                jobs.Calls.ShouldContain(c =>
                    c.JobName == seed.JobName
                    && c.JobType == seed.JobType
                    && c.TenantId == tenantId
                    && c.CronExpression == seed.CronExpression);
            }
        }
    }

    [Fact]
    public async Task SyncAllTenantsAsync_RunTwice_DoesNotDuplicateRecurringJobs()
    {
        var tenantId = Guid.NewGuid();
        var tenants = Substitute.For<IActiveTenantDirectory>();
        tenants.GetActiveTenantIdsAsync(Arg.Any<CancellationToken>()).Returns(new List<Guid> { tenantId });
        var jobs = new RecordingBackgroundJobService();
        var synchronizer = CreateSynchronizer(tenants, jobs);
        var seedCount = BackgroundJobSeeding.GetDefaultSeeds().Count();

        await synchronizer.SyncAllTenantsAsync(CancellationToken.None);
        await synchronizer.SyncAllTenantsAsync(CancellationToken.None);

        // Gerçek IBackgroundJobService.RecurringAsync implementasyonu Hangfire AddOrUpdate ile deterministik
        // "{jobName}_{tenantId:N}" kimliği kullanır; bu sahte de aynı semantiği taklit eder - toplam benzersiz
        // yinelenen iş sayısı ikinci koşuda artmaz.
        jobs.RecurringJobIds.Count.ShouldBe(seedCount);
        jobs.Calls.Count.ShouldBe(2 * seedCount);
    }

    [Fact]
    public async Task SyncAllTenantsAsync_WithNoActiveTenants_DoesNotCallRecurring()
    {
        var tenants = Substitute.For<IActiveTenantDirectory>();
        tenants.GetActiveTenantIdsAsync(Arg.Any<CancellationToken>()).Returns(new List<Guid>());
        var jobs = new RecordingBackgroundJobService();
        var synchronizer = CreateSynchronizer(tenants, jobs);

        var registered = await synchronizer.SyncAllTenantsAsync(CancellationToken.None);

        registered.ShouldBe(0);
        jobs.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task SyncAllTenantsAsync_WhenOneTenantsJobFailsItContinuesWithOthers()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var tenantC = Guid.NewGuid();
        var tenants = Substitute.For<IActiveTenantDirectory>();
        tenants.GetActiveTenantIdsAsync(Arg.Any<CancellationToken>()).Returns(new List<Guid> { tenantA, tenantB, tenantC });
        var jobs = new ThrowingBackgroundJobService(tenantB); // tenantB'nin ilk RecurringAsync çağrısında error
        var synchronizer = CreateSynchronizer(tenants, jobs);
        var seeds = BackgroundJobSeeding.GetDefaultSeeds().Count();

        var registered = await synchronizer.SyncAllTenantsAsync(CancellationToken.None);

        // tenantA: seeds.Count, tenantB: 0 (hata), tenantC: seeds.Count
        registered.ShouldBe(2 * seeds);
        // tenantA'nın tüm seed'leri kaydedilir, tenantB'nin sıfırı (hata yakalarız), tenantC'nin tüm seed'leri kaydedilir
        jobs.SuccessfulCalls.Count.ShouldBe(2 * seeds);
        jobs.FailedTenant.ShouldBe(tenantB);
    }

    private static BackgroundJobSeedSynchronizer CreateSynchronizer(IActiveTenantDirectory tenants, IBackgroundJobService jobs) =>
        new(tenants, jobs, NullLogger<BackgroundJobSeedSynchronizer>.Instance);

    private sealed class RecordingBackgroundJobService : IBackgroundJobService
    {
        public List<(string JobName, JobType JobType, Guid TenantId, string CronExpression, string Payload)> Calls { get; } = [];

        public HashSet<string> RecurringJobIds { get; } = [];

        public Task<string> EnqueueAsync(JobType jobType, Guid tenantId, string payload) => throw new NotSupportedException();

        public Task<string> ScheduleAsync(JobType jobType, Guid tenantId, string payload, TimeSpan delay) => throw new NotSupportedException();

        public Task RecurringAsync(string jobName, JobType jobType, Guid tenantId, string cronExpression, string payload)
        {
            Calls.Add((jobName, jobType, tenantId, cronExpression, payload));
            RecurringJobIds.Add($"{jobName}_{tenantId:N}");
            return Task.CompletedTask;
        }

        public Task RemoveRecurringAsync(string jobName) => throw new NotSupportedException();
    }

    private sealed class ThrowingBackgroundJobService : IBackgroundJobService
    {
        private readonly Guid _failingTenantId;
        private int _callCountForFailingTenant = 0;

        public ThrowingBackgroundJobService(Guid failingTenantId)
        {
            _failingTenantId = failingTenantId;
        }

        public List<(string JobName, JobType JobType, Guid TenantId, string CronExpression, string Payload)> SuccessfulCalls { get; } = [];

        public Guid FailedTenant => _failingTenantId;

        public Task<string> EnqueueAsync(JobType jobType, Guid tenantId, string payload) => throw new NotSupportedException();

        public Task<string> ScheduleAsync(JobType jobType, Guid tenantId, string payload, TimeSpan delay) => throw new NotSupportedException();

        public Task RecurringAsync(string jobName, JobType jobType, Guid tenantId, string cronExpression, string payload)
        {
            if (tenantId == _failingTenantId)
            {
                _callCountForFailingTenant++;
                if (_callCountForFailingTenant == 1)
                {
                    throw new InvalidOperationException($"Simulated error for tenant {tenantId}");
                }
            }

            SuccessfulCalls.Add((jobName, jobType, tenantId, cronExpression, payload));
            return Task.CompletedTask;
        }

        public Task RemoveRecurringAsync(string jobName) => throw new NotSupportedException();
    }
}

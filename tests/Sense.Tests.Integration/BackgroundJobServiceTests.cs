using Hangfire;
using Hangfire.InMemory;
using Microsoft.Extensions.DependencyInjection;
using Sense.Shared.Contracts.BackgroundJobs;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Notifications;
using Sense.Shared.Infrastructure.BackgroundJobs;
using Sense.Shared.Infrastructure.Context;
using Sense.Shared.Infrastructure.Notifications;
using Sense.Shared.Infrastructure.Observability;
using Xunit;

namespace Sense.Tests.Integration;

/// <summary>
/// Integration tests for background job service.
/// </summary>
public class BackgroundJobServiceTests : IAsyncLifetime
{
    private IBackgroundJobService _backgroundJobService = null!;
    private IBackgroundJobClient _jobClient = null!;
    private IRecurringJobManager _recurringJobManager = null!;
    private IServiceProvider _serviceProvider = null!;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();

        // Register in-memory Hangfire for testing
        services.AddHangfire(config => config.UseInMemoryStorage());
        services.AddScoped<IBackgroundJobClient>(sp => new BackgroundJobClient(
            JobStorage.Current ?? throw new InvalidOperationException("Hangfire storage not initialized")));
        services.AddScoped<IRecurringJobManager>(sp => new RecurringJobManager(
            JobStorage.Current ?? throw new InvalidOperationException("Hangfire storage not initialized")));

        // Register services
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ITenantContextSetter>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<CorrelationIdContext>();
        services.AddScoped<ICorrelationIdContextSetter>(sp => sp.GetRequiredService<CorrelationIdContext>());
        services.AddScoped<IBackgroundJobService, BackgroundJobService>();
        services.AddScoped<INotificationService, MockNotificationService>();
        services.AddScoped<IBackgroundJobExecutor, BackgroundJobExecutor>();
        services.AddLogging();

        _serviceProvider = services.BuildServiceProvider();
        _backgroundJobService = _serviceProvider.GetRequiredService<IBackgroundJobService>();
        _jobClient = _serviceProvider.GetRequiredService<IBackgroundJobClient>();
        _recurringJobManager = _serviceProvider.GetRequiredService<IRecurringJobManager>();

        // Initialize in-memory storage
        JobStorage.Current = new InMemoryStorage();

        await Task.CompletedTask;
    }

    [Fact]
    public async Task EnqueueAsync_ShouldReturnJobId()
    {
        // Arrange
        var jobType = JobType.SendEmail;
        var tenantId = Guid.NewGuid();
        var payload = """{"email":"test@example.com"}""";

        // Act
        var jobId = await _backgroundJobService.EnqueueAsync(jobType, tenantId, payload);

        // Assert
        Assert.NotNull(jobId);
        Assert.NotEmpty(jobId);
    }

    [Fact]
    public async Task ScheduleAsync_ShouldReturnJobId()
    {
        // Arrange
        var jobType = JobType.ExportReport;
        var tenantId = Guid.NewGuid();
        var payload = """{"reportId":"report123"}""";
        var delay = TimeSpan.FromMinutes(5);

        // Act
        var jobId = await _backgroundJobService.ScheduleAsync(jobType, tenantId, payload, delay);

        // Assert
        Assert.NotNull(jobId);
        Assert.NotEmpty(jobId);
    }

    [Fact]
    public async Task RecurringAsync_ShouldCreateRecurringJob()
    {
        // Arrange
        var jobName = "DailyAuditSummary";
        var jobType = JobType.DailyAuditSummary;
        var tenantId = Guid.NewGuid();
        var cronExpression = "0 1 * * *";
        var payload = """{"summaryType":"daily"}""";

        // Act - should not throw
        await _backgroundJobService.RecurringAsync(jobName, jobType, tenantId, cronExpression, payload);

        // Assert - Verify no exception was thrown
        Assert.True(true);
    }

    [Fact]
    public async Task RemoveRecurringAsync_ShouldRemoveRecurringJob()
    {
        // Arrange
        var jobName = "TestRecurringJob";
        var jobType = JobType.DailyAuditSummary;
        var tenantId = Guid.NewGuid();

        // First, create a recurring job
        await _backgroundJobService.RecurringAsync(jobName, jobType, tenantId, "0 1 * * *", "{}");

        // Act - should not throw
        await _backgroundJobService.RemoveRecurringAsync($"{jobName}_{tenantId:N}");

        // Assert - Verify no exception was thrown
        Assert.True(true);
    }

    public async Task DisposeAsync()
    {
        _serviceProvider?.Dispose();
        JobStorage.Current = null;
        await Task.CompletedTask;
    }

    /// <summary>
    /// Mock notification service for testing.
    /// </summary>
    private class MockNotificationService : INotificationService
    {
        public Task SendToTenantAsync(Guid tenantId, NotificationDto notification)
        {
            return Task.CompletedTask;
        }

        public Task BroadcastJobProgressAsync(Guid tenantId, JobProgressDto progress)
        {
            return Task.CompletedTask;
        }

        public Task BroadcastAuditAlertAsync(Guid tenantId, AuditAlertDto alert)
        {
            return Task.CompletedTask;
        }
    }
}

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Sense.Modules.Reporting.Api;
using Sense.Modules.Reporting.Application;
using Sense.Modules.Reporting.Contracts;
using Sense.Modules.Reporting.Domain;
using Sense.Modules.Reporting.Infrastructure.Jobs;
using Sense.Modules.Reporting.Infrastructure.Persistence;
using Sense.Shared.Contracts.BackgroundJobs;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Context;
using Sense.Shared.Infrastructure.Persistence.Outbox;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Reporting.Tests;

[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class ReportGenerationJobIntegrationTests : TestFixture
{
    private readonly MutablePermissions _permissions = new();
    private readonly MailerProbe _mailer = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        new ReportingModule().AddModule(services, configuration);
        services.AddSingleton<IPermissionService>(_permissions);
        services.AddSingleton<IDataScopeService, RequestDataScope>();
        services.AddSingleton<IAccessSettingsService, EnabledAccessSettings>();
        services.AddScoped<IReportProvider, RequestQueueTestProvider>();
        services.Replace(ServiceDescriptor.Singleton<IReportMailer>(_mailer));
    }

    protected override async Task OnDatabaseReadyAsync() =>
        await ServiceProvider!.GetRequiredService<ReportingDbContext>().Database.MigrateAsync();

    [Fact]
    public async Task Job_RejectsTenantMismatchAndRevokedRequesterPermissionWithoutGenerating()
    {
        var tenantId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var (executionId, request) = await SeedAsync(tenantId, requesterId, Guid.NewGuid());
        var probe = new GenerationProbe();

        await RunJobAsync(Guid.NewGuid(), request, probe);
        probe.Count.ShouldBe(0);
        (await ReadExecutionAsync(tenantId, executionId)).Status.ShouldBe(ReportExecutionStatus.Queued);

        await RunJobAsync(tenantId, request with { RequestedByUserId = Guid.NewGuid() }, probe);
        probe.Count.ShouldBe(0);
        (await ReadExecutionAsync(tenantId, executionId)).Status.ShouldBe(ReportExecutionStatus.Queued);

        _permissions.Set("leave.balances.read", false);
        await RunJobAsync(tenantId, request, probe);
        probe.Count.ShouldBe(0);
        (await ReadExecutionAsync(tenantId, executionId)).Status.ShouldBe(ReportExecutionStatus.Failed);
        (await ReadOutboxCountAsync(tenantId, "ReportReadyNotificationRequested")).ShouldBe(0);
    }

    [Fact]
    public async Task RequestHandler_PersistsExecutionAndOneGenerationRequestOutboxEventInTheSameDatabase()
    {
        var tenantId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        using var userScope = CurrentUserAccessor.Use(new AuthenticatedUser(requesterId, null, "person@example.test", "Requester",
            new HashSet<string>(), false, null, null, null, null));

        var result = await ExecuteCommandAsync(new RequestReportCommand("reporting.tests.queue", null, Notify: true));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(ReportExecutionStatus.Queued);
        using var scope = ServiceProvider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        var execution = await db.ReportExecutions.SingleAsync(e => e.Id == result.Value.Id);
        execution.RequestedByUserId.ShouldBe(requesterId);
        execution.Status.ShouldBe(ReportExecutionStatus.Queued);
        var generationEvents = await db.OutboxMessages.Where(m => m.Type.Contains("ReportGenerationRequested")).ToListAsync();
        generationEvents.Count.ShouldBe(1);
        generationEvents[0].TenantId.ShouldBe(tenantId);
        generationEvents[0].Payload.ShouldContain(result.Value.Id.ToString());
    }

    [Fact]
    public async Task Job_ConcurrentDuplicateDeliveryGeneratesOnceAndPersistsOneReadyEvent()
    {
        var tenantId = Guid.NewGuid();
        var (executionId, request) = await SeedAsync(tenantId, Guid.NewGuid(), Guid.NewGuid());
        var probe = new GenerationProbe(blockUntilReleased: true);

        var first = RunJobAsync(tenantId, request, probe);
        await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var duplicate = RunJobAsync(tenantId, request, probe);
        await duplicate;
        probe.Release.TrySetResult();
        await first;

        probe.Count.ShouldBe(1);
        (await ReadExecutionAsync(tenantId, executionId)).Status.ShouldBe(ReportExecutionStatus.Succeeded);
        (await ReadOutboxCountAsync(tenantId, "ReportReadyNotificationRequested")).ShouldBe(1);

        await RunJobAsync(tenantId, request, probe);
        probe.Count.ShouldBe(1);
        (await ReadOutboxCountAsync(tenantId, "ReportReadyNotificationRequested")).ShouldBe(1);
    }

    [Fact]
    public async Task Job_RecoversPersistedRunningExecutionAfterRestartAndMailOutboxRetriesIndependently()
    {
        var tenantId = Guid.NewGuid();
        var (executionId, request) = await SeedAsync(tenantId, Guid.NewGuid(), null, running: true);
        var probe = new GenerationProbe();

        await RunJobAsync(tenantId, request, probe);

        probe.Count.ShouldBe(1);
        (await ReadExecutionAsync(tenantId, executionId)).Status.ShouldBe(ReportExecutionStatus.Succeeded);
        (await ReadOutboxCountAsync(tenantId, "ReportReadyNotificationRequested")).ShouldBe(1);

        using (TenantContextSetter!.BeginScope(tenantId))
        using (var scope = ServiceProvider!.CreateScope())
        {
            var message = await scope.ServiceProvider.GetRequiredService<ReportingDbContext>().OutboxMessages
                .SingleAsync(m => m.Type.Contains("ReportReadyNotificationRequested"));
            var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor<ReportingDbContext>>();

            _mailer.FailNext = true;
            (await processor.ProcessAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
            var context = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            await context.Entry(message).ReloadAsync(TestContext.Current.CancellationToken);
            message.Attempts.ShouldBe(1);
            message.ProcessedAt.ShouldBeNull();
            message.NextAttemptAt = DateTime.UtcNow.AddMinutes(-1);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            (await processor.ProcessAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
            _mailer.Count.ShouldBe(2);
            (await processor.ProcessAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
            _mailer.Count.ShouldBe(2);
        }

        (await ReadExecutionAsync(tenantId, executionId)).Status.ShouldBe(ReportExecutionStatus.Succeeded);
    }

    private async Task<(Guid Id, ReportGenerationRequested Request)> SeedAsync(Guid tenantId, Guid requesterId, Guid? employeeId, bool running = false)
    {
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        using var scope = ServiceProvider!.CreateScope();
        var execution = ReportExecution.Create(tenantId, "leave.balance.detail", "{}", requesterId, DateTime.UtcNow).Value;
        if (running)
        {
            execution.Start(DateTime.UtcNow);
        }

        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        db.ReportExecutions.Add(execution);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var request = new ReportGenerationRequested(tenantId, execution.Id, requesterId, employeeId, "person@example.test", "Requester",
            "en-US", Notify: true, VisibleEmployeeIds: [Guid.NewGuid()], CanViewSensitive: false);
        return (execution.Id, request);
    }

    private async Task RunJobAsync(Guid tenantId, ReportGenerationRequested request, GenerationProbe probe)
    {
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        using var scope = ServiceProvider!.CreateScope();
        var services = scope.ServiceProvider;
        var provider = new ProbeReportProvider(services.GetRequiredService<ICurrentUser>(), services.GetRequiredService<ReportGenerationContext>(),
            request.RequestedByUserId, request.EmployeeId, request.VisibleEmployeeIds, probe);
        var handler = new ReportGenerationJob(
            services.GetRequiredService<ReportingDbContext>(), [provider], _permissions,
            services.GetRequiredService<ReportGenerationContext>(), services.GetRequiredService<ITenantContext>(),
            services.GetRequiredService<TimeProvider>(), services.GetRequiredService<ILogger<ReportGenerationJob>>(),
            services.GetRequiredService<IIntegrationEventOutbox>());
        await handler.ExecuteAsync(new ScheduledJobContext(tenantId, JsonSerializer.Serialize(request), DateTimeOffset.UtcNow), TestContext.Current.CancellationToken);
    }

    private async Task<ReportExecution> ReadExecutionAsync(Guid tenantId, Guid id)
    {
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        using var scope = ServiceProvider!.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ReportingDbContext>().ReportExecutions.SingleAsync(e => e.Id == id);
    }

    private async Task<int> ReadOutboxCountAsync(Guid tenantId, string typeFragment)
    {
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        using var scope = ServiceProvider!.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ReportingDbContext>().OutboxMessages.CountAsync(m => m.Type.Contains(typeFragment));
    }

    private sealed class MutablePermissions : IPermissionService
    {
        private readonly Dictionary<string, bool> _values = new(StringComparer.Ordinal);
        public void Set(string key, bool allowed) => _values[key] = allowed;
        public Task<bool> HasAsync(Guid userId, string permission, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.GetValueOrDefault(permission, true));
        public Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
        public Task<DataScope> GetScopeAsync(Guid userId, string permission, CancellationToken cancellationToken = default) =>
            Task.FromResult(DataScope.Company);
    }

    private sealed class RequestDataScope : IDataScopeService
    {
        public Task<IReadOnlySet<Guid>?> GetVisibleEmployeeIdsAsync(string permission, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<Guid>?>(new HashSet<Guid> { Guid.NewGuid() });
        public Task<bool> CanAccessEmployeeAsync(string permission, Guid employeeId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<IReadOnlySet<Guid>?> GetAccessibleOrgUnitIdsAsync(string permission, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<Guid>?>(null);
    }

    private sealed class EnabledAccessSettings : IAccessSettingsService
    {
        public Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> IsEnabledForCurrentUserAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class RequestQueueTestProvider : IReportProvider
    {
        public string Key => "reporting.tests.queue";
        public string ExecutionMode => "async";
        public string CategoryKey => ReportCategories.Administrative;
        public string? RequiredPermission => "leave.balances.read";
        public Task<ReportFileResult> GenerateAsync(Guid tenantId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken) =>
            Task.FromException<ReportFileResult>(new InvalidOperationException("Async reports must never run in the request handler."));
    }

    private sealed class GenerationProbe(bool blockUntilReleased = false)
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ReportFileResult> GenerateAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);
            Entered.TrySetResult();
            if (blockUntilReleased)
            {
                await Release.Task.WaitAsync(cancellationToken);
            }

            return new ReportFileResult("balances.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", [1, 2, 3]);
        }
    }

    private sealed class ProbeReportProvider(
        ICurrentUser currentUser, IReportGenerationContext generation, Guid expectedRequesterId, Guid? expectedEmployeeId,
        IReadOnlyList<Guid>? expectedVisibleEmployees, GenerationProbe probe) : IReportProvider
    {
        public string Key => "leave.balance.detail";
        public string ExecutionMode => "async";
        public string CategoryKey => ReportCategories.Administrative;
        public string? RequiredPermission => "leave.balances.read";

        public Task<ReportFileResult> GenerateAsync(Guid tenantId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
        {
            currentUser.UserId.ShouldBe(expectedRequesterId);
            currentUser.EmployeeId.ShouldBe(expectedEmployeeId);
            generation.IsBackground.ShouldBeTrue();
            generation.VisibleEmployeeIds.ShouldBe(expectedVisibleEmployees?.ToHashSet());
            return probe.GenerateAsync(cancellationToken);
        }
    }

    private sealed class MailerProbe : IReportMailer
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public bool FailNext { get; set; }

        public Task SendReportReadyAsync(string toEmail, string? toName, Guid executionId, string reportKey, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);
            if (FailNext)
            {
                FailNext = false;
                return Task.FromException(new InvalidOperationException("Transient mail failure"));
            }

            return Task.CompletedTask;
        }
    }
}

using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Sense.Shared.Contracts.BackgroundJobs;
using Sense.Shared.Contracts.Notifications;
using Sense.Shared.Infrastructure.BackgroundJobs;
using Sense.Shared.Infrastructure.Context;
using Sense.Shared.Infrastructure.Observability;
using Shouldly;
using Xunit;

namespace Sense.Shared.Infrastructure.Tests.BackgroundJobs;

/// <summary>ADR-0004: merkezi switch yerine DI ile keşfedilen <see cref="IScheduledJobHandler"/> dispatch'i.</summary>
public sealed class BackgroundJobExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_DispatchesToRegisteredHandler()
    {
        var handler = new RecordingJobHandler(JobType.SendEmail);
        var notificationService = Substitute.For<INotificationService>();
        var executor = CreateExecutor([handler], notificationService);
        var tenantId = Guid.NewGuid();

        await executor.ExecuteAsync(JobType.SendEmail, tenantId, """{"to":"test@example.com"}""");

        handler.Invocations.Count.ShouldBe(1);
        handler.Invocations[0].TenantId.ShouldBe(tenantId);
        handler.Invocations[0].PayloadJson.ShouldBe("""{"to":"test@example.com"}""");
        await notificationService.Received(1).SendToTenantAsync(tenantId, Arg.Any<NotificationDto>());
    }

    [Fact]
    public async Task ExecuteAsync_NoHandlerRegistered_DoesNotThrowAndSkips()
    {
        var notificationService = Substitute.For<INotificationService>();
        var executor = CreateExecutor([], notificationService);
        var tenantId = Guid.NewGuid();

        // Kayıtlı işleyici olmayan bir job türü no-op olmalı; sonuç yine de "success" olarak bildirilir
        // (executor'ın kendisi başarısız olmadı, yalnızca yapacak iş bulamadı).
        await Should.NotThrowAsync(() => executor.ExecuteAsync(JobType.PayrollRun, tenantId, "{}"));
    }

    [Fact]
    public void Constructor_DuplicateHandlerForSameJobType_ThrowsInvalidOperationException()
    {
        var first = new RecordingJobHandler(JobType.ExportReport);
        var second = new RecordingJobHandler(JobType.ExportReport);
        var notificationService = Substitute.For<INotificationService>();

        Should.Throw<InvalidOperationException>(() => CreateExecutor([first, second], notificationService));
    }

    private static BackgroundJobExecutor CreateExecutor(IEnumerable<IScheduledJobHandler> handlers, INotificationService notificationService)
    {
        var tenantContext = new TenantContext();
        var correlationIdContext = new CorrelationIdContext();
        return new BackgroundJobExecutor(
            tenantContext,
            correlationIdContext,
            notificationService,
            handlers,
            NullLogger<BackgroundJobExecutor>.Instance);
    }

    private sealed class RecordingJobHandler(JobType handles) : IScheduledJobHandler
    {
        public JobType Handles { get; } = handles;

        public List<ScheduledJobContext> Invocations { get; } = [];

        public Task ExecuteAsync(ScheduledJobContext context, CancellationToken ct)
        {
            Invocations.Add(context);
            return Task.CompletedTask;
        }
    }
}

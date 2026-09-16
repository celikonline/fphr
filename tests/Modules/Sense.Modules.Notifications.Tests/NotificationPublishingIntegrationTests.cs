using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Notifications.Application;
using Sense.Modules.Notifications.Application.Deliveries;
using Sense.Modules.Notifications.Application.Publishing;
using Sense.Modules.Notifications.Application.UserNotifications;
using Sense.Modules.Notifications.Contracts;
using Sense.Modules.Notifications.Domain.Deliveries;
using Sense.Modules.Notifications.Domain.Preferences;
using Sense.Modules.Notifications.Infrastructure;
using Sense.Modules.Notifications.Infrastructure.Delivery;
using Sense.Modules.Notifications.Infrastructure.Persistence;
using Sense.Modules.Workflow.Contracts;
using Sense.Shared.Contracts.Notifications;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Notifications.Tests;

/// <summary>
/// NotificationPublisher + EmailDeliveryProcessor against real PostgreSQL (Testcontainers): in-app rows and Pending
/// e-mail deliveries are written per recipient honouring preferences, re-publishing the same source event is a no-op,
/// the processor renders and sends (decision link minted just-in-time), and failures back off / skip as designed.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class NotificationPublishingIntegrationTests : TestFixture
{
    private const string BaseUrl = "https://app.senseik.test";

    private readonly RecordingEmailSender _emailSender = new();
    private readonly RecordingRealtimeNotifier _realtime = new();
    private readonly IApprovalDecisionLinkIssuer _linkIssuer = Substitute.For<IApprovalDecisionLinkIssuer>();

    protected override void ConfigureServices(IServiceCollection services)
    {
        var baseConfiguration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        var configuration = new ConfigurationBuilder()
            .AddConfiguration(baseConfiguration)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Frontend:BaseUrl"] = BaseUrl,
                ["Email:Provider"] = "None",
                ["Notifications:DefaultLanguage"] = "tr",
                ["Notifications:Delivery:MaxAttempts"] = "3",
            })
            .Build();

        // Registered before AddNotificationsCore: its IEmailSender registration is TryAdd.
        services.AddSingleton<IEmailSender>(_emailSender);
        services.AddNotificationsCore(configuration);
        services.AddSingleton<IRealtimeNotifier>(_realtime);
        services.AddSingleton(_linkIssuer);
    }

    protected override async Task OnDatabaseReadyAsync() =>
        await ServiceProvider!.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();

    [Fact]
    public async Task Publish_WritesInAppAndPendingEmail_PerRecipient_HonoursPreferences_AndIsIdempotent()
    {
        var tenantId = Guid.NewGuid();
        var withAccount = Guid.NewGuid();
        var optedOutOfEmail = Guid.NewGuid();
        var request = new NotificationRequest(
            Guid.NewGuid(),
            NotificationTemplateKeys.RequestCreated,
            [
                new NotificationRecipient(Guid.NewGuid(), withAccount, "Ayşe Yılmaz", "ayse@acme.test", AddressSources.WorkEmail, "tr"),
                new NotificationRecipient(Guid.NewGuid(), optedOutOfEmail, "John Doe", "john@acme.test", AddressSources.WorkEmail, "en"),
                new NotificationRecipient(Guid.NewGuid(), null, "No Account", "noaccount@acme.test", AddressSources.PersonalEmail, "de"),
            ],
            new Dictionary<string, string?> { [WorkflowTemplateModel.RequestType] = "Leave", [WorkflowTemplateModel.RequesterName] = "Ayşe Yılmaz" },
            WorkflowTemplateModel.InboxPath);

        using (TenantContextSetter!.BeginScope(tenantId))
        {
            using (var scope = ServiceProvider!.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
                db.Preferences.Add(new NotificationPreference(tenantId, optedOutOfEmail, NotificationTemplateKeys.RequestCreated, NotificationChannels.Email, false));
                await db.SaveChangesAsync();
            }

            await PublishAsync(tenantId, request);
            await PublishAsync(tenantId, request); // re-delivered integration event
        }

        using var verify = ServiceProvider!.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var inApp = await verifyDb.UserNotifications.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        inApp.Select(n => n.UserId).ShouldBe([withAccount, optedOutOfEmail], ignoreOrder: true);
        inApp.ShouldAllBe(n => n.TenantId == tenantId && n.ActionUrl == WorkflowTemplateModel.InboxPath && !n.IsRead && n.Title != string.Empty);

        var deliveries = await verifyDb.Deliveries.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        deliveries.Select(d => d.RecipientAddress).ShouldBe(["ayse@acme.test", "noaccount@acme.test"], ignoreOrder: true);
        deliveries.ShouldAllBe(d => d.Status == NotificationDeliveryStatus.Pending && d.Channel == NotificationChannels.Email);
        deliveries.Single(d => d.RecipientAddress == "noaccount@acme.test").Culture.ShouldBe("de");

        // Pushed once per created in-app row (not again on re-delivery).
        _realtime.Pushed.Select(p => p.UserId).ShouldBe([withAccount, optedOutOfEmail], ignoreOrder: true);
        _emailSender.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Processor_SendsDueEmails_MintsDecisionLink_SkipsStaleTasks_AndBacksOffOnFailure()
    {
        var tenantId = Guid.NewGuid();
        var liveTaskId = Guid.NewGuid();
        var staleTaskId = Guid.NewGuid();
        var decisionUrl = BaseUrl + "/approve/raw-token";
        _linkIssuer.IssueAsync(tenantId, liveTaskId, Arg.Any<CancellationToken>())
            .Returns(new ApprovalDecisionLink(decisionUrl, DateTime.UtcNow.AddDays(7)));
        _linkIssuer.IssueAsync(tenantId, staleTaskId, Arg.Any<CancellationToken>())
            .Returns((ApprovalDecisionLink?)null);
        _emailSender.FailFor.Add("broken@acme.test");

        using (TenantContextSetter!.BeginScope(tenantId))
        {
            await PublishAsync(tenantId, TaskAssigned(liveTaskId, "approver@acme.test"));
            await PublishAsync(tenantId, TaskAssigned(staleTaskId, "stale@acme.test"));
            await PublishAsync(tenantId, TaskAssigned(liveTaskId, "broken@acme.test"));
        }

        using (var scope = ServiceProvider!.CreateScope())
        {
            (await scope.ServiceProvider.GetRequiredService<EmailDeliveryProcessor>().ProcessAsync()).ShouldBe(3);
        }

        using (var scope = ServiceProvider!.CreateScope())
        {
            // Nothing is due any more: sent/skipped rows are final, the failed one waits for its backoff.
            (await scope.ServiceProvider.GetRequiredService<EmailDeliveryProcessor>().ProcessAsync()).ShouldBe(0);
        }

        var sent = _emailSender.Sent.ShouldHaveSingleItem();
        sent.ToAddress.ShouldBe("approver@acme.test");
        sent.Subject.ShouldNotBeNullOrWhiteSpace();
        sent.HtmlBody.ShouldNotBeNull();
        sent.HtmlBody.ShouldContain(decisionUrl);
        sent.TextBody.ShouldContain(decisionUrl);

        using var verify = ServiceProvider!.CreateScope();
        var deliveries = await verify.ServiceProvider.GetRequiredService<NotificationsDbContext>().Deliveries
            .IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(d => d.RecipientAddress);

        deliveries["approver@acme.test"].Status.ShouldBe(NotificationDeliveryStatus.Sent);
        deliveries["approver@acme.test"].SentAt.ShouldNotBeNull();

        deliveries["stale@acme.test"].Status.ShouldBe(NotificationDeliveryStatus.Skipped);
        deliveries["stale@acme.test"].LastError.ShouldBe(ApprovalDecisionLinkEnricher.TaskNoLongerActionable);

        var broken = deliveries["broken@acme.test"];
        broken.Status.ShouldBe(NotificationDeliveryStatus.Pending);
        broken.Attempts.ShouldBe(1);
        broken.LastError.ShouldNotBeNull();
        broken.LastError.ShouldContain("SMTP down");
        broken.NextAttemptAt.ShouldNotBeNull();
        broken.NextAttemptAt.Value.ShouldBeGreaterThan(DateTime.UtcNow);
    }

    private static NotificationRequest TaskAssigned(Guid taskId, string email) => new(
        Guid.NewGuid(),
        NotificationTemplateKeys.ApprovalTaskAssigned,
        [new NotificationRecipient(Guid.NewGuid(), null, "Mehmet Demir", email, AddressSources.WorkEmail, "en")],
        new Dictionary<string, string?>
        {
            [WorkflowTemplateModel.RequestType] = "Leave",
            [WorkflowTemplateModel.RequesterName] = "Ayşe Yılmaz",
            [WorkflowTemplateModel.StepNumber] = "1",
            [WorkflowTemplateModel.ApprovalTaskId] = taskId.ToString(),
            [WorkflowTemplateModel.IsDirectAssignee] = "true",
        },
        WorkflowTemplateModel.InboxPath);

    private async Task PublishAsync(Guid tenantId, NotificationRequest request)
    {
        using var scope = ServiceProvider!.CreateScope();
        await scope.ServiceProvider.GetRequiredService<NotificationPublisher>().PublishAsync(tenantId, request, CancellationToken.None);
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public ConcurrentQueue<EmailMessage> SentQueue { get; } = new();

        public IReadOnlyList<EmailMessage> Sent => [.. SentQueue];

        public HashSet<string> FailFor { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            if (FailFor.Contains(message.ToAddress))
            {
                throw new InvalidOperationException("SMTP down");
            }

            SentQueue.Enqueue(message);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingRealtimeNotifier : IRealtimeNotifier
    {
        private readonly ConcurrentQueue<(Guid UserId, UserNotificationDto Notification)> _pushed = new();

        public IReadOnlyList<(Guid UserId, UserNotificationDto Notification)> Pushed => [.. _pushed];

        public Task NotifyUserAsync(Guid userId, UserNotificationDto notification, CancellationToken ct = default)
        {
            _pushed.Enqueue((userId, notification));
            return Task.CompletedTask;
        }
    }
}

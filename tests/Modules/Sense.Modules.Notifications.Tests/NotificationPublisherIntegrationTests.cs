using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Notifications.Application;
using Sense.Modules.Notifications.Application.Notifications;
using Sense.Modules.Notifications.Application.Preferences;
using Sense.Modules.Notifications.Contracts;
using Sense.Modules.Notifications.Infrastructure;
using Sense.Modules.Notifications.Infrastructure.Persistence;
using Sense.Modules.Workflow.Contracts.Events;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Notifications;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Context;
using Sense.Shared.Kernel.Results;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Notifications.Tests;

/// <summary>
/// Real PostgreSQL (Testcontainers): publisher channel/preference rules, self-scoped queries/commands through the
/// Dispatcher pipeline (permission service substituted), and Workflow event → in-app notification via IEventBus.
/// </summary>
[Collection("PostgreSQL")]
public sealed class NotificationPublisherIntegrationTests : TestFixture
{
    private readonly IEmailSender _email = Substitute.For<IEmailSender>();
    private readonly IEmployeeDirectory _employees = Substitute.For<IEmployeeDirectory>();
    private readonly CapturingPusher _pusher = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;

        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        var accessSettings = Substitute.For<IAccessSettingsService>();
        accessSettings.IsEnabledAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        accessSettings.IsEnabledForCurrentUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        services.AddSingleton(permissions);
        services.AddSingleton(Substitute.For<IDataScopeService>());
        services.AddSingleton(accessSettings);
        services.AddSingleton(_email);
        services.AddSingleton(_employees);
        services.AddSingleton<INotificationRealtimePusher>(_pusher);
        services.AddNotificationsInfrastructure(configuration);
    }

    protected override async Task OnDatabaseReadyAsync() =>
        await ServiceProvider!.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();

    [Fact]
    public async Task Publish_AppliesPreferences_AndQueriesAreSelfScoped()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        // A turns e-mail off for "request approved".
        (await SendAs(userA, new UpdateMyNotificationPreferencesCommand(
            [new NotificationPreferenceInput(NotificationTemplateKeys.RequestApproved, EmailEnabled: false, InAppEnabled: true)]))).IsSuccess.ShouldBeTrue();

        var prefs = await QueryAs(userA, new GetMyNotificationPreferencesQuery());
        prefs.Value.Single(p => p.TemplateKey == NotificationTemplateKeys.RequestApproved).EmailEnabled.ShouldBeFalse();
        prefs.Value.Single(p => p.TemplateKey == NotificationTemplateKeys.RequestRejected).EmailEnabled.ShouldBeTrue();

        NotificationPublishResult published;
        using (var scope = ServiceProvider!.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<INotificationPublisher>().PublishAsync(new NotificationRequest(
                tenantId,
                [new NotificationRecipient(userA, "a@example.com", "A"), new NotificationRecipient(userB, "b@example.com", "B")],
                NotificationTemplateKeys.RequestApproved,
                new Dictionary<string, string> { [NotificationTemplateArgs.RequestType] = "Leave" },
                NotificationLinks.MyRequests));
            result.IsSuccess.ShouldBeTrue();
            published = result.Value;
        }

        published.InAppCreated.ShouldBe(2);
        published.EmailsSent.ShouldBe(1);
        await _email.Received(1).SendAsync(Arg.Is<EmailMessage>(m => m.ToAddress == "b@example.com" && m.Subject == "Talebiniz onaylandı"), Arg.Any<CancellationToken>());
        _pusher.Pushed.Select(p => p.UserId).ShouldBe([userA, userB], ignoreOrder: true);

        (await QueryAs(userA, new GetMyUnreadNotificationCountQuery())).Value.Count.ShouldBe(1);

        var previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        PagedResult<NotificationItemDto> listA;
        try
        {
            listA = (await QueryAs(userA, new ListMyNotificationsQuery(new PagedQuery(), UnreadOnly: true))).Value;
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }

        listA.TotalCount.ShouldBe(1);
        listA.Items[0].Title.ShouldBe("Your request was approved");
        listA.Items[0].Body.ShouldBe("Your leave request was approved.");
        listA.Items[0].Link.ShouldBe(NotificationLinks.MyRequests);

        var bNotificationId = _pusher.Pushed.Single(p => p.UserId == userB).Notification.Id;
        var foreign = await SendAs(userA, new MarkNotificationReadCommand(bNotificationId));
        foreign.IsFailure.ShouldBeTrue();
        foreign.Error.Type.ShouldBe(ErrorType.NotFound);

        (await SendAs(userA, new MarkNotificationReadCommand(listA.Items[0].Id))).IsSuccess.ShouldBeTrue();
        (await QueryAs(userA, new GetMyUnreadNotificationCountQuery())).Value.Count.ShouldBe(0);
        (await QueryAs(userB, new GetMyUnreadNotificationCountQuery())).Value.Count.ShouldBe(1);

        (await SendAs(userB, new MarkAllNotificationsReadCommand())).Value.ShouldBe(1);
        (await QueryAs(userB, new GetMyUnreadNotificationCountQuery())).Value.Count.ShouldBe(0);
    }

    [Fact]
    public async Task ApprovalCreatedAndRejectedEvents_ThroughEventBus_NotifyRequester()
    {
        var tenantId = Guid.NewGuid();
        var requesterEmployeeId = Guid.NewGuid();
        var requesterUserId = Guid.NewGuid();
        _employees.GetContactsAsync(tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new EmployeeContact(requesterEmployeeId, "Ayşe", "Yılmaz", "ayse@example.com", null, requesterUserId, "Active")]);

        var bus = ServiceProvider!.GetRequiredService<IEventBus>();
        await bus.Publish(new ApprovalRequestCreated(tenantId, Guid.NewGuid(), "Leave", Guid.NewGuid(), requesterEmployeeId));
        await bus.Publish(new ApprovalRejected(tenantId, Guid.NewGuid(), "Leave", Guid.NewGuid(), requesterEmployeeId, 1));

        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        using (var scope = ServiceProvider!.CreateScope())
        {
            var rows = await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Notifications.OrderBy(n => n.OccurredAt).ToListAsync();
            rows.Select(r => r.TemplateKey).ShouldBe([NotificationTemplateKeys.RequestCreated, NotificationTemplateKeys.RequestRejected], ignoreOrder: true);
            rows.ShouldAllBe(r => r.RecipientUserId == requesterUserId && r.TenantId == tenantId);
        }

        await _email.Received(1).SendAsync(
            Arg.Is<EmailMessage>(m => m.ToAddress == "ayse@example.com" && m.Subject == "Talebiniz oluşturuldu"), Arg.Any<CancellationToken>());
        await _email.Received(1).SendAsync(
            Arg.Is<EmailMessage>(m => m.ToAddress == "ayse@example.com" && m.TextBody.Contains("İzin talebiniz reddedildi.")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApprovalTaskAssignedEvent_NotifiesApproverInAppOnly()
    {
        var tenantId = Guid.NewGuid();
        var approverEmployeeId = Guid.NewGuid();
        var approverUserId = Guid.NewGuid();
        var requesterEmployeeId = Guid.NewGuid();
        _employees.GetContactsAsync(tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                new EmployeeContact(approverEmployeeId, "Mehmet", "Kaya", "mehmet@example.com", null, approverUserId, "Active"),
                new EmployeeContact(requesterEmployeeId, "Ayşe", "Yılmaz", "ayse@example.com", null, Guid.NewGuid(), "Active"),
            ]);

        var bus = ServiceProvider!.GetRequiredService<IEventBus>();
        await bus.Publish(new ApprovalTaskAssigned(tenantId, Guid.NewGuid(), Guid.NewGuid(), "Leave", requesterEmployeeId, 1, approverEmployeeId, null));
        await bus.Publish(new ApprovalTaskAssigned(tenantId, Guid.NewGuid(), Guid.NewGuid(), "Leave", requesterEmployeeId, 1, null, "HR"));

        _pusher.Pushed.Count.ShouldBe(1);
        _pusher.Pushed[0].UserId.ShouldBe(approverUserId);
        _pusher.Pushed[0].Notification.Body.ShouldBe("Ayşe Yılmaz tarafından oluşturulan İzin talebi onayınızı bekliyor.");
        _pusher.Pushed[0].Notification.Link.ShouldBe(NotificationLinks.Inbox);
        await _email.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
    }

    private async Task<Result> SendAs(Guid userId, ICommand command)
    {
        using var user = CurrentUserAccessor.Use(User(userId));
        using var scope = ServiceProvider!.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IDispatcher>().Send(command);
    }

    private async Task<Result<T>> SendAs<T>(Guid userId, ICommand<T> command)
    {
        using var user = CurrentUserAccessor.Use(User(userId));
        using var scope = ServiceProvider!.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IDispatcher>().Send(command);
    }

    private async Task<Result<T>> QueryAs<T>(Guid userId, IQuery<T> query)
    {
        using var user = CurrentUserAccessor.Use(User(userId));
        using var scope = ServiceProvider!.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IDispatcher>().Query(query);
    }

    private static AuthenticatedUser User(Guid userId) =>
        new(userId, null, $"{userId:N}@example.com", "user", new HashSet<string>(), false, null, null, Guid.NewGuid().ToString(), "127.0.0.1");

    private sealed class CapturingPusher : INotificationRealtimePusher
    {
        public List<(Guid UserId, NotificationItemDto Notification)> Pushed { get; } = [];

        public Task PushAsync(Guid recipientUserId, NotificationItemDto notification, CancellationToken ct)
        {
            Pushed.Add((recipientUserId, notification));
            return Task.CompletedTask;
        }
    }
}

using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Notifications.Application;
using Sense.Modules.Notifications.Infrastructure.Rendering;
using Sense.Modules.Notifications.Application.Publishing;
using Sense.Modules.Notifications.Contracts;
using Sense.Modules.Notifications.Domain;
using Sense.Modules.Notifications.Domain.Deliveries;
using Sense.Modules.Notifications.Domain.UserNotifications;
using Sense.Modules.Workflow.Contracts.Events;
using Shouldly;
using Xunit;

namespace Sense.Modules.Notifications.Tests;

/// <summary>workflow.delegation_assigned: the delegate is notified (email + in-app) with delegator name, period and inbox link.</summary>
public sealed class DelegationAssignedNotificationTests
{
    [Fact]
    public async Task Delegation_assigned_notifies_delegate_with_template_variables()
    {
        var tenantId = Guid.NewGuid();
        var delegator = Guid.NewGuid();
        var delegateEmployee = Guid.NewGuid();
        var delegateUser = Guid.NewGuid();

        var contacts = Substitute.For<IEmployeeContactLookup>();
        contacts.GetContactsAsync(tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<IReadOnlyCollection<Guid>>(1).Select(id => id == delegator
                ? new EmployeeContact(id, "Ayse", "Yilmaz", "ayse@acme.test", null, null, "Active")
                : new EmployeeContact(id, "Mehmet", "Demir", "mehmet@acme.test", null, delegateUser, "Active")).ToList());
        var profiles = Substitute.For<IUserProfileLookup>();
        profiles.GetAsync(tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, UserProfileInfo> { [delegateUser] = new(delegateUser, delegateEmployee, "m@acme.test", "Mehmet", "tr", true) });
        var resolver = new NotificationRecipientResolver(contacts, profiles, Substitute.For<IRoleAssignmentLookup>(), Options.Create(new NotificationsOptions()));

        var notifications = Substitute.For<IUserNotificationRepository>();
        var deliveries = Substitute.For<INotificationDeliveryRepository>();
        var preferences = Substitute.For<INotificationPreferenceRepository>();
        preferences.ListForUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
        UserNotification? inApp = null;
        NotificationDelivery? email = null;
        notifications.When(n => n.Add(Arg.Any<UserNotification>())).Do(c => inApp = c.Arg<UserNotification>());
        deliveries.When(d => d.Add(Arg.Any<NotificationDelivery>())).Do(c => email = c.Arg<NotificationDelivery>());
        var publisher = new NotificationPublisher(
            notifications, deliveries, preferences, Substitute.For<INotificationsUnitOfWork>(),
            new Sense.Modules.Notifications.Infrastructure.Rendering.ScribanNotificationTemplateRenderer(
                Options.Create(new NotificationsOptions()), Options.Create(new NotificationsFrontendOptions { BaseUrl = "https://app.senseik.test" })),
            Substitute.For<IRealtimeNotifier>(), TimeProvider.System, NullLogger<NotificationPublisher>.Instance);

        await new ApprovalDelegationAssignedNotificationHandler(resolver, publisher).Handle(
            new ApprovalDelegationAssigned(tenantId, Guid.NewGuid(), delegator, delegateEmployee, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 15)),
            CancellationToken.None);

        inApp.ShouldNotBeNull();
        inApp.UserId.ShouldBe(delegateUser);
        inApp.TemplateKey.ShouldBe(NotificationTemplateKeys.ApprovalDelegationAssigned);
        email.ShouldNotBeNull();
        email.RecipientAddress.ShouldBe("mehmet@acme.test");
        var model = JsonDocument.Parse(email.ModelJson).RootElement;
        model.GetProperty("delegator_name").GetString().ShouldBe("Ayse Yilmaz");
        model.GetProperty("period").GetString().ShouldBe("01.10.2026 - 15.10.2026");
        model.GetProperty("action_path").GetString().ShouldBe(WorkflowTemplateModel.InboxPath);
    }
}

using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Sense.Modules.Announcements.Contracts.Events;
using Sense.Modules.Documents.Contracts.Events;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Notifications.Application;
using Sense.Modules.Notifications.Application.Publishing;
using Sense.Modules.Notifications.Contracts;
using Sense.Modules.Notifications.Domain;
using Sense.Modules.Notifications.Domain.Deliveries;
using Sense.Modules.Notifications.Domain.UserNotifications;
using Sense.Modules.Notifications.Infrastructure;
using Sense.Modules.Notifications.Infrastructure.Realtime;
using Sense.Modules.Notifications.Infrastructure.Rendering;
using Sense.Modules.Payroll.Contracts;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Infrastructure.Notifications;
using Shouldly;
using Xunit;

namespace Sense.Modules.Notifications.Tests;

/// <summary>Consumers of the payslip follow-up events and the company event invitation (N1).</summary>
public sealed class PayslipAndEventNotificationTests
{
    private sealed class Harness
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public List<UserNotification> InApp { get; } = [];
        public List<NotificationDelivery> Emails { get; } = [];
        public IRoleAssignmentLookup Roles { get; } = Substitute.For<IRoleAssignmentLookup>();
        public NotificationRecipientResolver Resolver { get; }
        public NotificationPublisher Publisher { get; }

        /// <param name="contactFor">Employee id → contact (null user id = no account).</param>
        public Harness(Func<Guid, EmployeeContact> contactFor, Dictionary<Guid, UserProfileInfo>? profilesByUser = null)
        {
            var contacts = Substitute.For<IEmployeeContactLookup>();
            contacts.GetContactsAsync(TenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
                .Returns(call => call.ArgAt<IReadOnlyCollection<Guid>>(1).Select(contactFor).ToList());
            var profiles = Substitute.For<IUserProfileLookup>();
            profiles.GetAsync(TenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
                .Returns(profilesByUser ?? new Dictionary<Guid, UserProfileInfo>());
            Resolver = new NotificationRecipientResolver(contacts, profiles, Roles, Options.Create(new NotificationsOptions()));

            var notifications = Substitute.For<IUserNotificationRepository>();
            var deliveries = Substitute.For<INotificationDeliveryRepository>();
            var preferences = Substitute.For<INotificationPreferenceRepository>();
            preferences.ListForUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
            notifications.When(n => n.Add(Arg.Any<UserNotification>())).Do(c => InApp.Add(c.Arg<UserNotification>()));
            deliveries.When(d => d.Add(Arg.Any<NotificationDelivery>())).Do(c => Emails.Add(c.Arg<NotificationDelivery>()));
            Publisher = new NotificationPublisher(
                notifications, deliveries, preferences, Substitute.For<INotificationsUnitOfWork>(),
                new ScribanNotificationTemplateRenderer(
                    Options.Create(new NotificationsOptions()), Options.Create(new NotificationsFrontendOptions { BaseUrl = "https://app.senseik.test" })),
                Substitute.For<IRealtimeNotifier>(), TimeProvider.System, NullLogger<NotificationPublisher>.Instance);
        }

        public static JsonElement Model(NotificationDelivery d) => JsonDocument.Parse(d.ModelJson).RootElement;
    }

    private static EmployeeContact Contact(Guid id, string first, string last, string? email, Guid? userId = null) =>
        new(id, first, last, email, null, userId, "Active");

    [Fact]
    public async Task Event_invitation_notifies_only_the_listed_invitees()
    {
        var invitee = Guid.NewGuid();
        var user = Guid.NewGuid();
        var h = new Harness(id => Contact(id, "Mehmet", "Demir", "mehmet@acme.test", user),
            new() { [user] = new(user, invitee, "m@acme.test", "Mehmet", "tr", true) });

        await new CompanyEventInvitationNotificationHandler(h.Resolver, h.Publisher).Handle(
            new CompanyEventInvitationRequested(h.TenantId, Guid.NewGuid(), "Yaz Pikniği", new DateTime(2026, 10, 3, 10, 0, 0), null, false, "Bahçe", null, [invitee]),
            CancellationToken.None);

        h.InApp.ShouldHaveSingleItem().TemplateKey.ShouldBe(NotificationTemplateKeys.CompanyEventInvitation);
        var email = h.Emails.ShouldHaveSingleItem();
        email.RecipientAddress.ShouldBe("mehmet@acme.test");
        Harness.Model(email).GetProperty("event_title").GetString().ShouldBe("Yaz Pikniği");
        Harness.Model(email).GetProperty("action_path").GetString().ShouldBe("/app/company-calendar");
    }

    [Fact]
    public async Task Ack_reminder_notifies_each_employee_and_email_only_without_an_account()
    {
        var withUser = Guid.NewGuid();
        var withoutUser = Guid.NewGuid();
        var user = Guid.NewGuid();
        var h = new Harness(
            id => id == withUser ? Contact(id, "Ayse", "Yilmaz", "ayse@acme.test", user) : Contact(id, "Can", "Kaya", "can@acme.test"),
            new() { [user] = new(user, withUser, "a@acme.test", "Ayse", "tr", true) });

        await new PayslipAcknowledgementReminderNotificationHandler(h.Resolver, h.Publisher).Handle(
            new PayslipAcknowledgementReminderDue(h.TenantId,
            [
                new UnacknowledgedPayslip(Guid.NewGuid(), withUser, Guid.NewGuid(), 2026, 8),
                new UnacknowledgedPayslip(Guid.NewGuid(), withoutUser, Guid.NewGuid(), 2026, 8),
            ]),
            CancellationToken.None);

        h.InApp.ShouldHaveSingleItem().UserId.ShouldBe(user);
        h.Emails.Count.ShouldBe(2);
        h.Emails.Select(e => e.RecipientAddress).ShouldBe(["ayse@acme.test", "can@acme.test"], ignoreOrder: true);
        h.Emails.ShouldAllBe(e => e.TemplateKey == NotificationTemplateKeys.PayslipAcknowledgementReminder);
        Harness.Model(h.Emails[0]).GetProperty("period").GetString().ShouldBe("2026-08");
        Harness.Model(h.Emails[0]).GetProperty("action_path").GetString()!.ShouldEndWith("/payroll");
    }

    [Fact]
    public async Task Objection_accepted_notifies_the_target_role_members_with_employee_and_period()
    {
        var specialist = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var h = new Harness(id => id == owner ? Contact(id, "Ayse", "Yilmaz", "ayse@acme.test") : Contact(id, "Bora", "Uzman", "bora@acme.test"));
        h.Roles.ListEmployeeIdsInRoleAsync(h.TenantId, PayrollRoleCodes.PayrollSpecialist, Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { specialist });

        await new PayslipObjectionAcceptedNotificationHandler(h.Resolver, h.Publisher, h.Roles).Handle(
            new PayslipObjectionAccepted(h.TenantId, Guid.NewGuid(), owner, Guid.NewGuid(), 2026, 9, Guid.NewGuid(), PayrollRoleCodes.PayrollSpecialist),
            CancellationToken.None);

        var email = h.Emails.ShouldHaveSingleItem();
        email.RecipientAddress.ShouldBe("bora@acme.test");
        email.TemplateKey.ShouldBe(NotificationTemplateKeys.PayslipObjectionAccepted);
        var model = Harness.Model(email);
        model.GetProperty("employee_name").GetString().ShouldBe("Ayse Yilmaz");
        model.GetProperty("period").GetString().ShouldBe("2026-09");
        model.GetProperty("action_path").GetString().ShouldBe($"/app/employees/{owner}/payroll");
    }

    [Fact]
    public async Task Objection_accepted_with_an_empty_role_sends_nothing()
    {
        var h = new Harness(id => Contact(id, "X", "Y", "x@acme.test"));
        h.Roles.ListEmployeeIdsInRoleAsync(h.TenantId, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new List<Guid>());

        await new PayslipObjectionAcceptedNotificationHandler(h.Resolver, h.Publisher, h.Roles).Handle(
            new PayslipObjectionAccepted(h.TenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2026, 9, Guid.NewGuid(), PayrollRoleCodes.PayrollSpecialist),
            CancellationToken.None);

        h.Emails.ShouldBeEmpty();
        h.InApp.ShouldBeEmpty();
    }

    [Fact]
    public async Task Payslip_document_uploaded_notifies_the_employee_and_links_to_the_documents_tab()
    {
        var employee = Guid.NewGuid();
        var h = new Harness(id => Contact(id, "Ayse", "Yilmaz", "ayse@acme.test"));

        await new PayslipDocumentUploadedNotificationHandler(h.Resolver, h.Publisher).Handle(
            new PayslipDocumentUploaded(h.TenantId, employee, Guid.NewGuid(), 2026, 9), CancellationToken.None);

        var email = h.Emails.ShouldHaveSingleItem();
        email.TemplateKey.ShouldBe(NotificationTemplateKeys.PayslipDocumentUploaded);
        Harness.Model(email).GetProperty("action_path").GetString().ShouldBe($"/app/employees/{employee}/documents");
    }

    /// <summary>Sense.Worker calls AddNotificationsCore (Application assembly scanned by AddModuleHandlers): all four consumers must resolve there.</summary>
    [Fact]
    public void WorkerRegistration_ResolvesAllFourConsumers()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = "Host=localhost;Database=x;Username=x;Password=x",
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddSenseCore(configuration);
        services.AddSingleton(Substitute.For<IHubContext<NotificationsHub>>());
        services.AddScoped(_ => Substitute.For<IEmployeeContactLookup>());
        services.AddScoped(_ => Substitute.For<IUserProfileLookup>());
        services.AddScoped(_ => Substitute.For<IRoleAssignmentLookup>());
        services.AddScoped(_ => Substitute.For<IEmployeeAudienceLookup>());
        services.AddNotificationsCore(configuration);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetServices<IIntegrationEventHandler<CompanyEventInvitationRequested>>().ShouldHaveSingleItem().ShouldBeOfType<CompanyEventInvitationNotificationHandler>();
        scope.ServiceProvider.GetServices<IIntegrationEventHandler<PayslipAcknowledgementReminderDue>>().ShouldHaveSingleItem().ShouldBeOfType<PayslipAcknowledgementReminderNotificationHandler>();
        scope.ServiceProvider.GetServices<IIntegrationEventHandler<PayslipObjectionAccepted>>().ShouldHaveSingleItem().ShouldBeOfType<PayslipObjectionAcceptedNotificationHandler>();
        scope.ServiceProvider.GetServices<IIntegrationEventHandler<PayslipDocumentUploaded>>().ShouldHaveSingleItem().ShouldBeOfType<PayslipDocumentUploadedNotificationHandler>();
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Documents.Contracts.Events;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Notifications.Application;
using Sense.Modules.Notifications.Contracts;
using Sense.Modules.Notifications.Infrastructure;
using Sense.Modules.Notifications.Infrastructure.Persistence;
using Sense.Shared.Contracts.Events;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Notifications.Tests;

/// <summary>
/// Faz 1 tur 1: documents.expiry_reminder end-to-end - permission-based resolution (documents.requirements.view)
/// through to a persisted in-app UserNotification. Real PostgreSQL (Testcontainers); IRoleAssignmentLookup/
/// IEmployeeContactLookup/IUserProfileLookup are the cross-module contracts a real host wires up, substituted here.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class DocumentExpiryReminderDueNotificationHandlerTests : TestFixture
{
    private readonly IRoleAssignmentLookup _roleAssignments = Substitute.For<IRoleAssignmentLookup>();
    private readonly IEmployeeContactLookup _contacts = Substitute.For<IEmployeeContactLookup>();
    private readonly IUserProfileLookup _userProfiles = Substitute.For<IUserProfileLookup>();

    protected override void ConfigureServices(IServiceCollection services)
    {
        var baseConfiguration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        var configuration = new ConfigurationBuilder()
            .AddConfiguration(baseConfiguration)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Frontend:BaseUrl"] = "https://app.senseik.test",
                ["Email:Provider"] = "None",
                ["Notifications:DefaultLanguage"] = "tr",
            })
            .Build();

        services.AddSingleton(_roleAssignments);
        services.AddSingleton(_contacts);
        services.AddSingleton(_userProfiles);
        services.AddNotificationsCore(configuration);
        // After AddNotificationsCore: its AddScoped IRealtimeNotifier (SignalR, needs a host HubContext) would
        // otherwise win single-service resolution; the handler only needs the port, and these tests assert
        // persisted in-app notifications, not realtime delivery.
        services.AddSingleton(Substitute.For<IRealtimeNotifier>());
    }

    protected override async Task OnDatabaseReadyAsync() =>
        await ServiceProvider!.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();

    [Fact]
    public async Task Handle_NotifiesEveryoneWithDocumentsRequirementsView_InApp()
    {
        var tenantId = Guid.NewGuid();
        var hrEmployeeId = Guid.NewGuid();
        var hrUserId = Guid.NewGuid();

        _roleAssignments.ListEmployeeIdsInRoleAsync(tenantId, "HR", Arg.Any<CancellationToken>()).Returns([hrEmployeeId]);
        _roleAssignments.ListEmployeeIdsInRoleAsync(tenantId, Arg.Is<string>(r => r != "HR"), Arg.Any<CancellationToken>()).Returns([]);
        _contacts.GetContactsAsync(tenantId, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(hrEmployeeId)), Arg.Any<CancellationToken>())
            .Returns([new EmployeeContact(hrEmployeeId, "Ayşe", "Yılmaz", "ayse@acme.test", null, hrUserId, "Active")]);
        _userProfiles.GetAsync(tenantId, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(hrUserId)), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, UserProfileInfo> { [hrUserId] = new(hrUserId, hrEmployeeId, "ayse@acme.test", "Ayşe Yılmaz", "tr", true) });

        using (TenantContextSetter!.BeginScope(tenantId))
        {
            using var scope = ServiceProvider!.CreateScope();
            var handlers = scope.ServiceProvider.GetServices<IIntegrationEventHandler<DocumentExpiryReminderDue>>().ToList();
            handlers.ShouldHaveSingleItem();
            await handlers[0].Handle(new DocumentExpiryReminderDue(tenantId, ExpiringCount: 3, ExpiredCount: 1), CancellationToken.None);
        }

        using var verify = ServiceProvider!.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var notification = await db.UserNotifications.IgnoreQueryFilters().AsNoTracking().SingleAsync(n => n.TenantId == tenantId);
        notification.UserId.ShouldBe(hrUserId);
        notification.TemplateKey.ShouldBe(NotificationTemplateKeys.DocumentExpiryReminder);
        notification.ActionUrl.ShouldBe("/app/settings/company/missing-documents?tab=expiring");
        notification.Body.ShouldContain("3");
        notification.Body.ShouldContain("1");
    }

    [Fact]
    public async Task Handle_BothCountsZero_DoesNotNotifyAnyone()
    {
        var tenantId = Guid.NewGuid();

        using (TenantContextSetter!.BeginScope(tenantId))
        {
            using var scope = ServiceProvider!.CreateScope();
            var handler = scope.ServiceProvider.GetServices<IIntegrationEventHandler<DocumentExpiryReminderDue>>().Single();
            await handler.Handle(new DocumentExpiryReminderDue(tenantId, ExpiringCount: 0, ExpiredCount: 0), CancellationToken.None);
        }

        await _roleAssignments.DidNotReceive().ListEmployeeIdsInRoleAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        using var verify = ServiceProvider!.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        (await db.UserNotifications.IgnoreQueryFilters().AsNoTracking().AnyAsync(n => n.TenantId == tenantId)).ShouldBeFalse();
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Assets.Contracts.Events;
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
/// assets.assignment_created / assets.assignment_returned / assets.unreturned_at_termination end-to-end - the
/// assigned/returning employee's own in-app UserNotification (and the assets.assets.manage holders' one for an
/// unreturned asset at exit), through the same NotificationRecipientResolver used by the employee lifecycle handlers.
/// Real PostgreSQL (Testcontainers).
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class AssetsEventHandlerTests : TestFixture
{
    private readonly IEmployeeContactLookup _contacts = Substitute.For<IEmployeeContactLookup>();
    private readonly IUserProfileLookup _userProfiles = Substitute.For<IUserProfileLookup>();
    private readonly IRoleAssignmentLookup _roleAssignments = Substitute.For<IRoleAssignmentLookup>();

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

        services.AddSingleton(_contacts);
        services.AddSingleton(_userProfiles);
        services.AddSingleton(_roleAssignments);
        services.AddNotificationsCore(configuration);
        // Same reasoning as DocumentExpiryReminderDueNotificationHandlerTests: the scoped SignalR IRealtimeNotifier
        // from AddNotificationsCore needs a host HubContext; these tests only assert the persisted UserNotification.
        services.AddSingleton(Substitute.For<IRealtimeNotifier>());
    }

    protected override async Task OnDatabaseReadyAsync() =>
        await ServiceProvider!.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();

    [Fact]
    public async Task AssetAssigned_NotifiesTheAssignedEmployee_InApp()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        SetUpEmployee(tenantId, employeeId, userId);

        using (TenantContextSetter!.BeginScope(tenantId))
        {
            using var scope = ServiceProvider!.CreateScope();
            var handlers = scope.ServiceProvider.GetServices<IIntegrationEventHandler<AssetAssigned>>().ToList();
            handlers.ShouldHaveSingleItem();
            await handlers[0].Handle(
                new AssetAssigned(tenantId, Guid.NewGuid(), employeeId, new DateOnly(2026, 9, 22)),
                CancellationToken.None);
        }

        using var verify = ServiceProvider!.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var notification = await db.UserNotifications.IgnoreQueryFilters().AsNoTracking().SingleAsync(n => n.TenantId == tenantId);
        notification.UserId.ShouldBe(userId);
        notification.TemplateKey.ShouldBe(NotificationTemplateKeys.AssetAssigned);
        notification.ActionUrl.ShouldBe("/app/assets");
        notification.Body.ShouldContain("22.09.2026");
        // No AssetName on the event (e.g. queued before the field existed): the nameless wording, nothing dangling.
        notification.Title.ShouldBe("Size yeni bir zimmet verildi");
        notification.Body.ShouldBe("Size 22.09.2026 tarihinde yeni bir zimmet verildi.");
    }

    [Fact]
    public async Task AssetAssigned_WithAssetName_NamesTheEquipment_InApp()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        SetUpEmployee(tenantId, employeeId, Guid.NewGuid());

        using (TenantContextSetter!.BeginScope(tenantId))
        {
            using var scope = ServiceProvider!.CreateScope();
            var handler = scope.ServiceProvider.GetServices<IIntegrationEventHandler<AssetAssigned>>().Single();
            await handler.Handle(
                new AssetAssigned(tenantId, Guid.NewGuid(), employeeId, new DateOnly(2026, 9, 22), "MacBook Pro 14"),
                CancellationToken.None);
        }

        var notification = await SingleNotificationAsync(tenantId);
        notification.Title.ShouldBe("Size zimmet verildi: MacBook Pro 14");
        notification.Body.ShouldBe("Size 22.09.2026 tarihinde MacBook Pro 14 zimmeti verildi.");
    }

    [Fact]
    public async Task AssetReturned_NotifiesTheReturningEmployee_InApp()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        SetUpEmployee(tenantId, employeeId, userId);

        using (TenantContextSetter!.BeginScope(tenantId))
        {
            using var scope = ServiceProvider!.CreateScope();
            var handlers = scope.ServiceProvider.GetServices<IIntegrationEventHandler<AssetReturned>>().ToList();
            handlers.ShouldHaveSingleItem();
            await handlers[0].Handle(
                new AssetReturned(tenantId, Guid.NewGuid(), employeeId, new DateOnly(2026, 9, 22)),
                CancellationToken.None);
        }

        using var verify = ServiceProvider!.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var notification = await db.UserNotifications.IgnoreQueryFilters().AsNoTracking().SingleAsync(n => n.TenantId == tenantId);
        notification.UserId.ShouldBe(userId);
        notification.TemplateKey.ShouldBe(NotificationTemplateKeys.AssetReturned);
        notification.Body.ShouldContain("22.09.2026");
        notification.Title.ShouldBe("Zimmet iadeniz kaydedildi");
        notification.Body.ShouldBe("22.09.2026 tarihli zimmet iadeniz kaydedildi.");
    }

    [Fact]
    public async Task AssetReturned_WithAssetName_NamesTheEquipment_InApp()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        SetUpEmployee(tenantId, employeeId, Guid.NewGuid());

        using (TenantContextSetter!.BeginScope(tenantId))
        {
            using var scope = ServiceProvider!.CreateScope();
            var handler = scope.ServiceProvider.GetServices<IIntegrationEventHandler<AssetReturned>>().Single();
            await handler.Handle(
                new AssetReturned(tenantId, Guid.NewGuid(), employeeId, new DateOnly(2026, 9, 22), "MacBook Pro 14"),
                CancellationToken.None);
        }

        var notification = await SingleNotificationAsync(tenantId);
        notification.Title.ShouldBe("Zimmet iadeniz kaydedildi: MacBook Pro 14");
        notification.Body.ShouldBe("22.09.2026 tarihli MacBook Pro 14 zimmet iadeniz kaydedildi.");
    }

    [Fact]
    public async Task AssetAssigned_EmployeeHasNoUserAccount_DoesNotThrow_AndCreatesNoNotification()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        _contacts.GetContactsAsync(tenantId, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(employeeId)), Arg.Any<CancellationToken>())
            .Returns([new EmployeeContact(employeeId, "Can", "Öz", "can@acme.test", null, null, "Active")]);

        using (TenantContextSetter!.BeginScope(tenantId))
        {
            using var scope = ServiceProvider!.CreateScope();
            var handler = scope.ServiceProvider.GetServices<IIntegrationEventHandler<AssetAssigned>>().Single();
            await handler.Handle(new AssetAssigned(tenantId, Guid.NewGuid(), employeeId, new DateOnly(2026, 9, 22)), CancellationToken.None);
        }

        using var verify = ServiceProvider!.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        (await db.UserNotifications.IgnoreQueryFilters().AsNoTracking().AnyAsync(n => n.TenantId == tenantId)).ShouldBeFalse();
    }

    [Fact]
    public async Task AssetUnreturnedAtTermination_NotifiesHolderOfAssetsManage_InApp()
    {
        var tenantId = Guid.NewGuid();
        var leaverId = Guid.NewGuid();
        var hrEmployeeId = Guid.NewGuid();
        var hrUserId = Guid.NewGuid();

        _contacts.GetContactsAsync(tenantId, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(leaverId)), Arg.Any<CancellationToken>())
            .Returns([new EmployeeContact(leaverId, "Ayşe", "Yılmaz", "ayse@acme.test", null, null, "Terminated")]);
        SetUpEmployee(tenantId, hrEmployeeId, hrUserId);
        _roleAssignments.ListEmployeeIdsInRoleAsync(tenantId, "HR", Arg.Any<CancellationToken>()).Returns([hrEmployeeId]);
        _roleAssignments.ListEmployeeIdsInRoleAsync(tenantId, Arg.Is<string>(r => r != "HR"), Arg.Any<CancellationToken>()).Returns([]);

        using (TenantContextSetter!.BeginScope(tenantId))
        {
            using var scope = ServiceProvider!.CreateScope();
            var handlers = scope.ServiceProvider.GetServices<IIntegrationEventHandler<AssetUnreturnedAtTermination>>().ToList();
            handlers.ShouldHaveSingleItem();
            await handlers[0].Handle(new AssetUnreturnedAtTermination(tenantId, Guid.NewGuid(), leaverId), CancellationToken.None);
        }

        using var verify = ServiceProvider!.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var notification = await db.UserNotifications.IgnoreQueryFilters().AsNoTracking().SingleAsync(n => n.TenantId == tenantId);
        notification.UserId.ShouldBe(hrUserId);
        notification.TemplateKey.ShouldBe(NotificationTemplateKeys.AssetUnreturnedAtTermination);
        notification.Title.ShouldContain("Ayşe Yılmaz");
        notification.Body.ShouldContain("Ayşe Yılmaz");
        notification.Title.ShouldBe("İade edilmemiş zimmet: Ayşe Yılmaz");
        notification.Body.ShouldBe("Ayşe Yılmaz işten ayrıldı, ancak iade edilmemiş bir zimmeti var.");
    }

    [Fact]
    public async Task AssetUnreturnedAtTermination_WithAssetName_KeepsPerAssetNotificationsDistinguishable_InApp()
    {
        var tenantId = Guid.NewGuid();
        var leaverId = Guid.NewGuid();
        var hrEmployeeId = Guid.NewGuid();

        _contacts.GetContactsAsync(tenantId, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(leaverId)), Arg.Any<CancellationToken>())
            .Returns([new EmployeeContact(leaverId, "Ayşe", "Yılmaz", "ayse@acme.test", null, null, "Terminated")]);
        SetUpEmployee(tenantId, hrEmployeeId, Guid.NewGuid());
        _roleAssignments.ListEmployeeIdsInRoleAsync(tenantId, "HR", Arg.Any<CancellationToken>()).Returns([hrEmployeeId]);
        _roleAssignments.ListEmployeeIdsInRoleAsync(tenantId, Arg.Is<string>(r => r != "HR"), Arg.Any<CancellationToken>()).Returns([]);

        using (TenantContextSetter!.BeginScope(tenantId))
        {
            using var scope = ServiceProvider!.CreateScope();
            var handler = scope.ServiceProvider.GetServices<IIntegrationEventHandler<AssetUnreturnedAtTermination>>().Single();
            await handler.Handle(new AssetUnreturnedAtTermination(tenantId, Guid.NewGuid(), leaverId, "MacBook Pro 14"), CancellationToken.None);
            await handler.Handle(new AssetUnreturnedAtTermination(tenantId, Guid.NewGuid(), leaverId, "iPhone 15"), CancellationToken.None);
        }

        using var verify = ServiceProvider!.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var notifications = await db.UserNotifications.IgnoreQueryFilters().AsNoTracking().Where(n => n.TenantId == tenantId).ToListAsync();
        notifications.Count.ShouldBe(2);
        notifications.Select(n => n.Title).ShouldBe(
            ["İade edilmemiş zimmet: Ayşe Yılmaz (MacBook Pro 14)", "İade edilmemiş zimmet: Ayşe Yılmaz (iPhone 15)"], ignoreOrder: true);
        notifications.ShouldAllBe(n => n.Body.Contains("zimmeti iade edilmedi"));
    }

    [Fact]
    public async Task AssetUnreturnedAtTermination_NoOneHoldsAssetsManage_CreatesNoNotification()
    {
        var tenantId = Guid.NewGuid();
        _roleAssignments.ListEmployeeIdsInRoleAsync(tenantId, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);

        using (TenantContextSetter!.BeginScope(tenantId))
        {
            using var scope = ServiceProvider!.CreateScope();
            var handler = scope.ServiceProvider.GetServices<IIntegrationEventHandler<AssetUnreturnedAtTermination>>().Single();
            await handler.Handle(new AssetUnreturnedAtTermination(tenantId, Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);
        }

        using var verify = ServiceProvider!.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        (await db.UserNotifications.IgnoreQueryFilters().AsNoTracking().AnyAsync(n => n.TenantId == tenantId)).ShouldBeFalse();
    }

    private async Task<Sense.Modules.Notifications.Domain.UserNotifications.UserNotification> SingleNotificationAsync(Guid tenantId)
    {
        using var verify = ServiceProvider!.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        return await db.UserNotifications.IgnoreQueryFilters().AsNoTracking().SingleAsync(n => n.TenantId == tenantId);
    }

    private void SetUpEmployee(Guid tenantId, Guid employeeId, Guid userId)
    {
        _contacts.GetContactsAsync(tenantId, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(employeeId)), Arg.Any<CancellationToken>())
            .Returns([new EmployeeContact(employeeId, "Can", "Öz", "can@acme.test", null, userId, "Active")]);
        _userProfiles.GetAsync(tenantId, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(userId)), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, UserProfileInfo> { [userId] = new(userId, employeeId, "can@acme.test", "Can Öz", "tr", true) });
    }
}

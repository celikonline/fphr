using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Notifications.Application;
using Sense.Modules.Notifications.Application.Publishing;
using Sense.Modules.Notifications.Contracts;
using Sense.Modules.Notifications.Infrastructure;
using Sense.Modules.Notifications.Infrastructure.Persistence;
using Sense.Modules.Notifications.Infrastructure.Rendering;
using Sense.Modules.Workflow.Contracts.Events;
using Sense.Shared.Contracts.Events;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Notifications.Tests;

/// <summary>
/// FG-009 request.comment_added end-to-end: Workflow's ApprovalRequestCommented → an in-app UserNotification for every
/// listed party except the author (defensive, even if Workflow listed them), linking to /app/requests/{id}.
/// Real PostgreSQL (Testcontainers); cross-module lookups substituted.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class ApprovalRequestCommentedNotificationHandlerTests : TestFixture
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
        // After AddNotificationsCore, like DocumentExpiryReminderDueNotificationHandlerTests: no SignalR hub in this host.
        services.AddSingleton(Substitute.For<IRealtimeNotifier>());
    }

    protected override async Task OnDatabaseReadyAsync() =>
        await ServiceProvider!.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();

    [Fact]
    public async Task Handle_NotifiesEveryListedPartyInApp_ExceptTheAuthor_WithTheRequestDetailPath()
    {
        var tenantId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var (author, authorUser) = (Guid.NewGuid(), Guid.NewGuid());
        var (approver, approverUser) = (Guid.NewGuid(), Guid.NewGuid());
        var (requester, requesterUser) = (Guid.NewGuid(), Guid.NewGuid());
        var people = new Dictionary<Guid, (Guid UserId, string First, string Last, string Language)>
        {
            [author] = (authorUser, "Ayşe", "Yılmaz", "tr"),
            [approver] = (approverUser, "Mehmet", "Demir", "tr"),
            [requester] = (requesterUser, "John", "Doe", "en"),
        };
        _contacts.GetContactsAsync(tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ((IReadOnlyCollection<Guid>)ci[1]).Where(people.ContainsKey)
                .Select(id => new EmployeeContact(id, people[id].First, people[id].Last, null, null, people[id].UserId, "Active"))
                .ToList());
        _userProfiles.GetAsync(tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(ci => people.Where(p => ((IReadOnlyCollection<Guid>)ci[1]).Contains(p.Value.UserId))
                .ToDictionary(p => p.Value.UserId, p => new UserProfileInfo(p.Value.UserId, p.Key, "user@acme.test", $"{p.Value.First} {p.Value.Last}", p.Value.Language, true)));

        using (TenantContextSetter!.BeginScope(tenantId))
        {
            using var scope = ServiceProvider!.CreateScope();
            var handler = scope.ServiceProvider.GetServices<IIntegrationEventHandler<ApprovalRequestCommented>>().ShouldHaveSingleItem();
            await handler.Handle(
                new ApprovalRequestCommented(tenantId, requestId, "Leave", Guid.NewGuid(), requester, author, [approver, requester, author]),
                CancellationToken.None);
        }

        using var verify = ServiceProvider!.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var notifications = await db.UserNotifications.IgnoreQueryFilters().AsNoTracking().Where(n => n.TenantId == tenantId).ToListAsync(TestContext.Current.CancellationToken);
        notifications.Select(n => n.UserId).ShouldBe([approverUser, requesterUser], ignoreOrder: true);
        notifications.ShouldAllBe(n => n.TemplateKey == NotificationTemplateKeys.RequestCommentAdded && n.ActionUrl == $"/app/requests/{requestId}");
        var toApprover = notifications.Single(n => n.UserId == approverUser);
        toApprover.Title.ShouldBe("İzin talebine yeni yorum");
        toApprover.Body.ShouldBe("Ayşe Yılmaz bir yorum yazdı.");
        notifications.Single(n => n.UserId == requesterUser).Title.ShouldBe("New comment: Leave request");

        // In-app only: no e-mail/push delivery rows.
        (await db.Deliveries.IgnoreQueryFilters().AsNoTracking().AnyAsync(d => d.TenantId == tenantId, TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    [Fact]
    public void Template_WithoutAuthorName_FallsBackToAGenericBody_InEveryLanguage()
    {
        var renderer = new ScribanNotificationTemplateRenderer(
            Options.Create(new NotificationsOptions()),
            Options.Create(new NotificationsFrontendOptions { BaseUrl = "https://app.senseik.test" }));
        var model = new Dictionary<string, object?>(StringComparer.Ordinal) { [WorkflowTemplateModel.RequestType] = "Overtime" };

        renderer.Render(NotificationTemplateKeys.RequestCommentAdded, NotificationChannels.InApp, "tr", model).Body
            .ShouldBe("Talebe yeni bir yorum yazıldı.");
        renderer.Render(NotificationTemplateKeys.RequestCommentAdded, NotificationChannels.InApp, "de", model).Subject
            .ShouldBe("Neuer Kommentar zum Antrag (Überstunden)");
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Notifications.Application;
using Sense.Modules.Notifications.Application.HrMessages;
using Sense.Modules.Notifications.Application.Publishing;
using Sense.Modules.Notifications.Contracts;
using Sense.Modules.Notifications.Domain.HrMessages;
using Sense.Modules.Notifications.Infrastructure;
using Sense.Modules.Notifications.Infrastructure.Persistence;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Notifications;
using Sense.Shared.Kernel.Results;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Notifications.Tests.HrMessages;

/// <summary>E6 HR messages against real PostgreSQL: send, recipient resolution, inbox/outbox scoping, SMS gate.</summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class HrMessageTests : TestFixture
{
    private readonly IEmployeeDirectory _directory = Substitute.For<IEmployeeDirectory>();
    private readonly IEmployeeContactLookup _contacts = Substitute.For<IEmployeeContactLookup>();
    private readonly IUserProfileLookup _profiles = Substitute.For<IUserProfileLookup>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Dictionary<Guid, EmployeeContact> _employees = [];

    protected override void ConfigureServices(IServiceCollection services)
    {
        var baseConfiguration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        var configuration = new ConfigurationBuilder()
            .AddConfiguration(baseConfiguration)
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Email:Provider"] = "None" })
            .Build();

        services.AddNotificationsCore(configuration);
        services.AddSingleton<IRealtimeNotifier>(Substitute.For<IRealtimeNotifier>());
        services.AddSingleton(_directory);
        services.AddSingleton(_contacts);
        services.AddSingleton(_profiles);
        services.AddSingleton(Substitute.For<IRoleAssignmentLookup>());
        services.AddSingleton(_currentUser);
        services.AddSingleton(Substitute.For<Sense.Modules.Workflow.Contracts.IApprovalDecisionLinkIssuer>());
        services.AddSingleton(Substitute.For<Sense.Modules.Payroll.Contracts.IPayslipDocumentProvider>());
        services.AddSingleton(Substitute.For<IWorkCertificateProvider>());

        _contacts.GetContactsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<IReadOnlyCollection<Guid>>(1).Where(_employees.ContainsKey).Select(id => _employees[id]).ToList());
        _profiles.GetAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => (IReadOnlyDictionary<Guid, UserProfileInfo>)call.ArgAt<IReadOnlyCollection<Guid>>(1)
                .ToDictionary(id => id, id => new UserProfileInfo(id, null, $"{id}@acme.test", "User", "tr", true)));
    }

    protected override async Task OnDatabaseReadyAsync() =>
        await ServiceProvider!.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();

    private (Guid EmployeeId, Guid UserId) AddEmployee(string name)
    {
        var employeeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _employees[employeeId] = new EmployeeContact(employeeId, name, "Test", $"{name}@acme.test", null, userId, "Active");
        return (employeeId, userId);
    }

    private static SendHrMessageCommand Send(EmployeeSelection? selection, IReadOnlyList<Guid>? groups = null, string[]? channels = null,
        HrMessageImportance importance = HrMessageImportance.Normal, string body = "Merhaba") =>
        new("Konu", body, importance, channels ?? [NotificationChannels.InApp], selection, groups);

    private async Task<Result<SendHrMessageResult>> RunAsync(Guid tenantId, Guid? asUser, SendHrMessageCommand command)
    {
        _currentUser.UserId.Returns(asUser);
        using var scope = ServiceProvider!.CreateScope();
        using var tenant = TenantContextSetter!.BeginScope(tenantId);
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<SendHrMessageCommand, SendHrMessageResult>>();
        return await handler.Handle(command, CancellationToken.None);
    }

    [Fact]
    public void Empty_recipient_selection_rejected()
    {
        var validator = new SendHrMessageValidator();

        validator.Validate(Send(null)).IsValid.ShouldBeFalse();
        validator.Validate(Send(new EmployeeSelection([], null, null))).IsValid.ShouldBeFalse();
        validator.Validate(Send(null, [])).IsValid.ShouldBeFalse();
        validator.Validate(Send(new EmployeeSelection([Guid.NewGuid()], null, null))).IsValid.ShouldBeTrue();
        validator.Validate(Send(null, [Guid.NewGuid()])).IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task Empty_resolution_is_rejected_by_handler()
    {
        var tenantId = Guid.NewGuid();
        _directory.ResolveSelectionAsync(tenantId, Arg.Any<EmployeeSelection>(), Arg.Any<CancellationToken>()).Returns([]);

        var result = await RunAsync(tenantId, Guid.NewGuid(), Send(new EmployeeSelection(null, new EmployeeFilter("nobody", null, false, null, null, null), null)));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(NotificationsErrors.RecipientsRequired);
    }

    [Fact]
    public async Task Group_snapshot_resolves_at_send_time()
    {
        var tenantId = Guid.NewGuid();
        var sender = Guid.NewGuid();
        var (firstEmployee, _) = AddEmployee("Ayse");
        var (secondEmployee, _) = AddEmployee("Mehmet");
        var groupSelection = new EmployeeSelection(null, new EmployeeFilter("it", null, false, null, null, null), null);
        Guid groupId;
        using (TenantContextSetter!.BeginScope(tenantId))
        {
            using var scope = ServiceProvider!.CreateScope();
            var group = new MessageGroup(tenantId, "IT", HrMessageSelection.Serialize(groupSelection));
            scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().MessageGroups.Add(group);
            await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().SaveChangesAsync();
            groupId = group.Id;
        }

        // Membership at group-creation time was {first}; by send time it is {first, second}.
        _directory.ResolveSelectionAsync(tenantId, Arg.Is<EmployeeSelection>(s => s.Filter != null && s.Filter.Q == "it"), Arg.Any<CancellationToken>())
            .Returns([firstEmployee, secondEmployee]);

        var result = await RunAsync(tenantId, sender, Send(null, [groupId]));

        result.IsSuccess.ShouldBeTrue();
        result.Value.RecipientCount.ShouldBe(2);
        using var verify = ServiceProvider!.CreateScope();
        var recipients = await verify.ServiceProvider.GetRequiredService<NotificationsDbContext>().HrMessageRecipients
            .IgnoreQueryFilters().AsNoTracking().Where(r => r.MessageId == result.Value.Id).ToListAsync();
        recipients.Select(r => r.EmployeeId).ShouldBe([firstEmployee, secondEmployee], ignoreOrder: true);
    }

    [Fact]
    public async Task Sms_channel_rejected_when_null_sender()
    {
        var tenantId = Guid.NewGuid();
        var (employeeId, _) = AddEmployee("Ayse");
        _directory.ResolveSelectionAsync(tenantId, Arg.Any<EmployeeSelection>(), Arg.Any<CancellationToken>()).Returns([employeeId]);

        var result = await RunAsync(tenantId, Guid.NewGuid(), Send(new EmployeeSelection([employeeId], null, null), channels: [NotificationChannels.InApp, "sms"]));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(NotificationsErrors.SmsChannelUnavailable);
        new Sense.Shared.Infrastructure.Notifications.NullSmsSender(Microsoft.Extensions.Logging.Abstractions.NullLogger<Sense.Shared.Infrastructure.Notifications.NullSmsSender>.Instance).IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public async Task Inbox_and_outbox_scope_by_user()
    {
        var tenantId = Guid.NewGuid();
        var sender = Guid.NewGuid();
        var (employeeA, userA) = AddEmployee("Ayse");
        var (employeeB, userB) = AddEmployee("Mehmet");
        _directory.ResolveSelectionAsync(tenantId, Arg.Any<EmployeeSelection>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<EmployeeSelection>(1).EmployeeIds!);

        (await RunAsync(tenantId, sender, Send(new EmployeeSelection([employeeA], null, null)))).IsSuccess.ShouldBeTrue();
        (await RunAsync(tenantId, sender, Send(new EmployeeSelection([employeeB], null, null)))).IsSuccess.ShouldBeTrue();

        using var scope = ServiceProvider!.CreateScope();
        using var tenant = TenantContextSetter!.BeginScope(tenantId);
        var repo = scope.ServiceProvider.GetRequiredService<Sense.Modules.Notifications.Domain.IHrMessageRepository>();

        (await repo.ListInboxAsync(userA, 1, 20)).TotalCount.ShouldBe(1);
        (await repo.ListInboxAsync(userB, 1, 20)).TotalCount.ShouldBe(1);
        (await repo.ListInboxAsync(sender, 1, 20)).TotalCount.ShouldBe(0);
        (await repo.ListSentAsync(sender, 1, 20)).TotalCount.ShouldBe(2);
        (await repo.ListSentAsync(userA, 1, 20)).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Importance_high_marks_delivery_priority()
    {
        var tenantId = Guid.NewGuid();
        var (employeeId, _) = AddEmployee("Ayse");
        _directory.ResolveSelectionAsync(tenantId, Arg.Any<EmployeeSelection>(), Arg.Any<CancellationToken>()).Returns([employeeId]);

        var result = await RunAsync(tenantId, Guid.NewGuid(),
            Send(new EmployeeSelection([employeeId], null, null), channels: [NotificationChannels.Email], importance: HrMessageImportance.High));

        result.IsSuccess.ShouldBeTrue();
        using var verify = ServiceProvider!.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var message = await db.HrMessages.IgnoreQueryFilters().AsNoTracking().SingleAsync(m => m.Id == result.Value.Id);
        message.IsHighPriority.ShouldBeTrue();
        var delivery = await db.Deliveries.IgnoreQueryFilters().AsNoTracking().SingleAsync(d => d.SourceEventId == result.Value.Id);
        System.Text.Json.JsonDocument.Parse(delivery.ModelJson).RootElement.GetProperty("importance").GetString().ShouldBe("high");
        // Only the chosen channel is used: no in-app row for an e-mail-only message.
        (await db.UserNotifications.IgnoreQueryFilters().AnyAsync(n => n.SourceEventId == result.Value.Id)).ShouldBeFalse();
    }

    [Fact]
    public void Content_over_4096_rejected()
    {
        var validator = new SendHrMessageValidator();
        var selection = new EmployeeSelection([Guid.NewGuid()], null, null);

        validator.Validate(Send(selection, body: new string('a', 4097))).IsValid.ShouldBeFalse();
        validator.Validate(Send(selection, body: new string('a', 4096))).IsValid.ShouldBeTrue();
        Should.Throw<ArgumentException>(() => new HrMessage(
            Guid.NewGuid(), Guid.NewGuid(), "s", new string('a', 4097), HrMessageImportance.Normal, [NotificationChannels.InApp]));
    }
}

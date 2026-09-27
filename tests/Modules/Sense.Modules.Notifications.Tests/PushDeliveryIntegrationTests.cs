using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Notifications.Application;
using Sense.Modules.Notifications.Application.Publishing;
using Sense.Modules.Notifications.Contracts;
using Sense.Modules.Notifications.Domain.Deliveries;
using Sense.Modules.Notifications.Domain.PushDevices;
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
/// Push delivery (11.7, push-bildirimleri-tasarim.md §3): NotificationPublisher only opens a "push" delivery row for
/// push-enabled templates, EmailDeliveryProcessor must not claim those rows (the mandatory precondition fix), and
/// PushDeliveryProcessor sends them through IExpoPushSender - revoking the device on DeviceNotRegistered and
/// skipping when the user has no active device. Real PostgreSQL (Testcontainers); Expo itself is a fake, no HTTP call
/// ever leaves the process.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class PushDeliveryIntegrationTests : TestFixture
{
    private const string BaseUrl = "https://app.senseik.test";

    private readonly FakeExpoPushSender _expoSender = new();

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
            })
            .Build();

        services.AddSingleton<IEmailSender>(Substitute.For<IEmailSender>());
        services.AddNotificationsCore(configuration);
        services.AddSingleton<IRealtimeNotifier>(Substitute.For<IRealtimeNotifier>());
        // Host-provided PDF ports behind the payslip / work certificate attachment providers (EmailDeliveryProcessor
        // constructs EmailDeliverySender eagerly, which needs these resolvable even though nothing sends here).
        services.AddSingleton(Substitute.For<Sense.Modules.Payroll.Contracts.IPayslipDocumentProvider>());
        services.AddSingleton(Substitute.For<Sense.Modules.Employee.Contracts.IWorkCertificateProvider>());
        services.AddSingleton(Substitute.For<IApprovalDecisionLinkIssuer>());
        // Registered after AddNotificationsCore's AddHttpClient<IExpoPushSender, ExpoPushSender>: last registration wins.
        services.AddSingleton<IExpoPushSender>(_expoSender);
    }

    protected override async Task OnDatabaseReadyAsync() =>
        await ServiceProvider!.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();

    [Fact]
    public async Task EmailDeliveryProcessor_DoesNotClaim_PushChannelDeliveries()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await SeedDeliveryAsync(tenantId, userId, NotificationChannels.Push);

        using (var scope = ServiceProvider!.CreateScope())
        {
            var processed = await scope.ServiceProvider.GetRequiredService<EmailDeliveryProcessor>().ProcessAsync();
            processed.ShouldBe(0);
        }

        var delivery = await SingleDeliveryAsync();
        delivery.Status.ShouldBe(NotificationDeliveryStatus.Pending);
        delivery.Attempts.ShouldBe(0);
    }

    [Fact]
    public async Task PushDeliveryProcessor_SendsToActiveDevices_AndMarksSent()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await SeedDeliveryAsync(tenantId, userId, NotificationChannels.Push);
        await SeedActiveDeviceAsync(tenantId, userId, "ExponentPushToken[sent-ok]");

        _expoSender.Respond = messages => messages.Select(m => new ExpoPushTicket(m.Token, IsError: false, ErrorCode: null)).ToList();

        using (var scope = ServiceProvider!.CreateScope())
        {
            var processed = await scope.ServiceProvider.GetRequiredService<PushDeliveryProcessor>().ProcessAsync();
            processed.ShouldBe(1);
        }

        var delivery = await SingleDeliveryAsync();
        delivery.Status.ShouldBe(NotificationDeliveryStatus.Sent);
        delivery.SentAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task PushDeliveryProcessor_RevokesToken_WhenExpoReportsDeviceNotRegistered()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        const string staleToken = "ExponentPushToken[stale]";

        await SeedDeliveryAsync(tenantId, userId, NotificationChannels.Push);
        await SeedActiveDeviceAsync(tenantId, userId, staleToken);

        _expoSender.Respond = messages => messages.Select(m => new ExpoPushTicket(m.Token, IsError: true, ErrorCode: ExpoPushErrorCodes.DeviceNotRegistered)).ToList();

        using (var scope = ServiceProvider!.CreateScope())
        {
            (await scope.ServiceProvider.GetRequiredService<PushDeliveryProcessor>().ProcessAsync()).ShouldBe(1);
        }

        using var verify = ServiceProvider!.CreateScope();
        var device = await verify.ServiceProvider.GetRequiredService<NotificationsDbContext>().PushDeviceTokens
            .IgnoreQueryFilters().AsNoTracking().SingleAsync(d => d.Token == staleToken);
        device.RevokedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task PushDeliveryProcessor_SkipsDelivery_WhenUserHasNoActiveDevice()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await SeedDeliveryAsync(tenantId, userId, NotificationChannels.Push);
        // No device seeded.

        using (var scope = ServiceProvider!.CreateScope())
        {
            (await scope.ServiceProvider.GetRequiredService<PushDeliveryProcessor>().ProcessAsync()).ShouldBe(1);
        }

        var delivery = await SingleDeliveryAsync();
        delivery.Status.ShouldBe(NotificationDeliveryStatus.Skipped);
        delivery.LastError.ShouldBe("no_active_device");
    }

    [Fact]
    public async Task Publisher_OpensPushDeliveryRow_OnlyForPushEnabledTemplate()
    {
        var tenantId = Guid.NewGuid();
        var pushEnabledUserId = Guid.NewGuid();
        var notPushEnabledUserId = Guid.NewGuid();

        var pushEnabledRequest = new NotificationRequest(
            Guid.NewGuid(),
            NotificationTemplateKeys.RequestApproved, // push-enabled (NotificationCatalog)
            [new NotificationRecipient(Guid.NewGuid(), pushEnabledUserId, "Ayşe Yılmaz", "ayse@acme.test", AddressSources.WorkEmail, "tr")],
            new Dictionary<string, string?>(),
            ActionPath: null);

        var notPushEnabledRequest = new NotificationRequest(
            Guid.NewGuid(),
            NotificationTemplateKeys.RequestCreated, // email + in-app only
            [new NotificationRecipient(Guid.NewGuid(), notPushEnabledUserId, "John Doe", "john@acme.test", AddressSources.WorkEmail, "en")],
            new Dictionary<string, string?>(),
            ActionPath: null);

        using (TenantContextSetter!.BeginScope(tenantId))
        {
            using var scope = ServiceProvider!.CreateScope();
            var publisher = scope.ServiceProvider.GetRequiredService<NotificationPublisher>();
            await publisher.PublishAsync(tenantId, pushEnabledRequest, CancellationToken.None);
            await publisher.PublishAsync(tenantId, notPushEnabledRequest, CancellationToken.None);
        }

        using var verify = ServiceProvider!.CreateScope();
        var deliveries = await verify.ServiceProvider.GetRequiredService<NotificationsDbContext>().Deliveries
            .IgnoreQueryFilters().AsNoTracking().Where(d => d.Channel == NotificationChannels.Push).ToListAsync();

        var pushRow = deliveries.ShouldHaveSingleItem();
        pushRow.RecipientUserId.ShouldBe(pushEnabledUserId);
        pushRow.RecipientAddress.ShouldBe(pushEnabledUserId.ToString());
        pushRow.AddressSource.ShouldBe(AddressSources.UserId);
    }

    private async Task SeedDeliveryAsync(Guid tenantId, Guid userId, string channel)
    {
        using var scope = ServiceProvider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        using (TenantContextSetter!.BeginScope(tenantId))
        {
            db.Deliveries.Add(new NotificationDelivery(
                tenantId, Guid.NewGuid(), NotificationTemplateKeys.RequestApproved, channel,
                null, userId, userId.ToString(), "Test User", AddressSources.UserId, "tr", "{}", DateTime.UtcNow));
            await db.SaveChangesAsync();
        }
    }

    private async Task SeedActiveDeviceAsync(Guid tenantId, Guid userId, string token)
    {
        using var scope = ServiceProvider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        using (TenantContextSetter!.BeginScope(tenantId))
        {
            db.PushDeviceTokens.Add(new PushDeviceToken(tenantId, userId, token, PushPlatforms.Ios, null, DateTime.UtcNow));
            await db.SaveChangesAsync();
        }
    }

    private async Task<NotificationDelivery> SingleDeliveryAsync()
    {
        using var scope = ServiceProvider!.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Deliveries
            .IgnoreQueryFilters().AsNoTracking().SingleAsync();
    }

    private sealed class FakeExpoPushSender : IExpoPushSender
    {
        public Func<IReadOnlyList<ExpoPushMessage>, IReadOnlyList<ExpoPushTicket>> Respond { get; set; } =
            messages => messages.Select(m => new ExpoPushTicket(m.Token, IsError: false, ErrorCode: null)).ToList();

        public Task<IReadOnlyList<ExpoPushTicket>> SendAsync(IReadOnlyList<ExpoPushMessage> messages, CancellationToken cancellationToken = default) =>
            Task.FromResult(Respond(messages));
    }
}

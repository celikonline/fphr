using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Sense.Modules.Announcements.Application;
using Sense.Modules.Announcements.Application.Support;
using Sense.Modules.Announcements.Contracts.Events;
using Sense.Modules.Announcements.Domain;
using Sense.Modules.Announcements.Domain.Support;
using Sense.Modules.Announcements.Infrastructure.Support;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Xunit;

namespace Sense.Modules.Announcements.Tests;

public sealed class SupportPortalBridgeTests
{
    private static SupportTicket Ticket() => SupportTicket.Create(Guid.NewGuid(), Guid.NewGuid(), "Test subject", "Test body", SupportTicketCategory.General).Value;
    private static SupportPortalOptions Options(string url = "https://support.example/") => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["FP_SUPPORT_PORTAL_URL"] = url, ["FP_SUPPORT_TENANT_ID"] = Guid.NewGuid().ToString(),
        ["FP_SUPPORT_PROJECT_ID"] = Guid.NewGuid().ToString(), ["FP_SUPPORT_API_KEY"] = "test-secret"
    }).Build());

    [Fact]
    public async Task Create_UsesPersistedScopeAndStableIdempotencyKey()
    {
        var ticket = Ticket();
        var remote = Guid.NewGuid();
        var handler = new RecordingHandler($"{{\"id\":\"{remote}\"}}");
        using var client = new HttpClient(handler);
        var bridge = new SupportPortalBridge(client, Options());
        Assert.Equal(remote, await bridge.CreateAsync(ticket, CancellationToken.None));
        Assert.Equal(remote, await bridge.CreateAsync(ticket, CancellationToken.None));
        Assert.Equal(2, handler.Requests.Count);
        foreach (var request in handler.Requests)
        {
            Assert.Equal(ticket.Id.ToString("D"), request.Key);
            Assert.Equal($"tenant:{ticket.TenantId:D}:user:{ticket.RequestedByUserId:D}", request.Subject);
            Assert.Equal("Bearer test-secret", request.Authorization);
            using var json = JsonDocument.Parse(request.Body!);
            Assert.Equal(ticket.Body, json.RootElement.GetProperty("description").GetString());
            Assert.Equal("fphr", json.RootElement.GetProperty("sourceProduct").GetString());
            Assert.Equal(ticket.Id.ToString("D"), json.RootElement.GetProperty("externalReference").GetString());
        }
    }

    [Fact]
    public async Task Read_OnlyReturnsPublicComments_AndFailureKeepsLocalRecord()
    {
        var ticket = Ticket();
        ticket.AttachExternalRef($"fp-support:{Guid.NewGuid()}");
        var handler = new RecordingHandler("""{"id":"remote","number":"FP-1","status":"in_progress","comments":[{"id":"1","body":"public reply","visibility":"public","createdAt":"2026-10-14T10:00:00Z","author":{"name":"Support"}},{"id":"2","body":"internal note","visibility":"internal","createdAt":"2026-10-14T10:00:00Z"}]}""");
        using var client = new HttpClient(handler);
        var bridge = new SupportPortalBridge(client, Options());
        var detail = await bridge.ReadAsync(ticket, CancellationToken.None);
        Assert.Equal("connected", detail.SyncState);
        Assert.Equal("Support", Assert.Single(detail.Comments).AuthorName);
        handler.Status = HttpStatusCode.BadGateway;
        var tenant = Substitute.For<ITenantContext>(); tenant.TenantId.Returns(ticket.TenantId);
        var currentUser = Substitute.For<ICurrentUser>(); currentUser.UserId.Returns(ticket.RequestedByUserId);
        var tickets = Substitute.For<ISupportTicketRepository>();
        tickets.GetByIdAsync(ticket.TenantId, ticket.Id, Arg.Any<CancellationToken>()).Returns(ticket);
        var query = new GetSupportTicketHandler(tenant, currentUser, Substitute.For<IPermissionService>(), tickets, bridge);
        var result = await query.Handle(new(ticket.Id), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(ticket.Body, result.Value.Body);
        Assert.Equal("unavailable", result.Value.Portal!.SyncState);
    }

    [Fact]
    public async Task NonOwner_CannotReadOrWritePortal_EvenWithManagePermissionForComment()
    {
        var ticket = Ticket(); ticket.AttachExternalRef($"fp-support:{Guid.NewGuid()}");
        var tenant = Substitute.For<ITenantContext>(); tenant.TenantId.Returns(ticket.TenantId);
        var user = Substitute.For<ICurrentUser>(); user.UserId.Returns(Guid.NewGuid());
        var repository = Substitute.For<ISupportTicketRepository>();
        repository.GetByIdAsync(ticket.TenantId, ticket.Id, Arg.Any<CancellationToken>()).Returns(ticket);
        var bridge = Substitute.For<ISupportPortalBridge>(); bridge.Enabled.Returns(true);
        var read = new GetSupportTicketHandler(tenant, user, Substitute.For<IPermissionService>(), repository, bridge);
        Assert.True((await read.Handle(new(ticket.Id), CancellationToken.None)).IsFailure);
        var write = new AddSupportTicketCommentHandler(tenant, user, repository, bridge);
        Assert.True((await write.Handle(new(ticket.Id, "Reply", Guid.NewGuid()), CancellationToken.None)).IsFailure);
        await bridge.DidNotReceive().ReadAsync(Arg.Any<SupportTicket>(), Arg.Any<CancellationToken>());
        await bridge.DidNotReceive().AddCommentAsync(Arg.Any<SupportTicket>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delivery_AttachesRemoteId_AndRetrySkipsAlreadyConnected()
    {
        var ticket = Ticket(); var remote = Guid.NewGuid();
        var repository = Substitute.For<ISupportTicketRepository>();
        repository.GetByIdAsync(ticket.TenantId, ticket.Id, Arg.Any<CancellationToken>()).Returns(ticket);
        var bridge = Substitute.For<ISupportPortalBridge>(); bridge.Enabled.Returns(true);
        bridge.CreateAsync(ticket, Arg.Any<CancellationToken>()).Returns(remote);
        var channel = Substitute.For<ISupportChannel>(); var uow = Substitute.For<IAnnouncementsUnitOfWork>();
        var handler = new SupportTicketCreatedHandler(channel, bridge, repository, uow);
        var message = new SupportTicketCreated(ticket.TenantId, ticket.Id, ticket.RequestedByUserId, ticket.Subject, "General");
        await handler.Handle(message, CancellationToken.None);
        await handler.Handle(message, CancellationToken.None);
        Assert.Equal($"fp-support:{remote:D}", ticket.ExternalRef);
        await bridge.Received(1).CreateAsync(ticket, Arg.Any<CancellationToken>());
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.True(ticket.Respond("Local response").IsFailure);
        Assert.True(ticket.Close().IsFailure);
    }

    [Fact]
    public async Task DeliveryFailure_ThrowsSafeExceptionForOutboxRetry_WithoutSecretOrBody()
    {
        var ticket = Ticket();
        using var client = new HttpClient(new RecordingHandler("test-secret Test body") { Status = HttpStatusCode.TemporaryRedirect });
        var bridge = new SupportPortalBridge(client, Options());
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.CreateAsync(ticket, CancellationToken.None));
        Assert.DoesNotContain("test-secret", exception.ToString());
        Assert.DoesNotContain(ticket.Body, exception.ToString());
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public async Task DisabledDelivery_UsesExistingEmailChannel()
    {
        var ticket = Ticket();
        var channel = Substitute.For<ISupportChannel>();
        var bridge = Substitute.For<ISupportPortalBridge>();
        var handler = new SupportTicketCreatedHandler(channel, bridge, Substitute.For<ISupportTicketRepository>(), Substitute.For<IAnnouncementsUnitOfWork>());
        await handler.Handle(new(ticket.TenantId, ticket.Id, ticket.RequestedByUserId, ticket.Subject, "General"), CancellationToken.None);
        await channel.Received(1).NotifyTicketCreatedAsync(ticket.Id, ticket.Subject, "General", Arg.Any<CancellationToken>());
        await bridge.DidNotReceive().CreateAsync(Arg.Any<SupportTicket>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EventDeliveryFailure_PropagatesToOutboxWithoutEmailFallback()
    {
        var ticket = Ticket(); var channel = Substitute.For<ISupportChannel>();
        var repository = Substitute.For<ISupportTicketRepository>();
        repository.GetByIdAsync(ticket.TenantId, ticket.Id, Arg.Any<CancellationToken>()).Returns(ticket);
        var bridge = Substitute.For<ISupportPortalBridge>(); bridge.Enabled.Returns(true);
        bridge.CreateAsync(ticket, Arg.Any<CancellationToken>()).Returns(Task.FromException<Guid>(new InvalidOperationException("Support portal request failed.")));
        var uow = Substitute.For<IAnnouncementsUnitOfWork>();
        var handler = new SupportTicketCreatedHandler(channel, bridge, repository, uow);
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(new(ticket.TenantId, ticket.Id, ticket.RequestedByUserId, ticket.Subject, "General"), CancellationToken.None));
        Assert.Null(ticket.ExternalRef);
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await channel.DidNotReceive().NotifyTicketCreatedAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OwnerFollowup_UsesRemoteTicketPathAndProvidedUuid()
    {
        var ticket = Ticket(); var remote = Guid.NewGuid(); var key = Guid.NewGuid();
        ticket.AttachExternalRef($"fp-support:{remote}");
        var recorder = new RecordingHandler("{}");
        using var client = new HttpClient(recorder);
        var bridge = new SupportPortalBridge(client, Options());
        var tenant = Substitute.For<ITenantContext>(); tenant.TenantId.Returns(ticket.TenantId);
        var user = Substitute.For<ICurrentUser>(); user.UserId.Returns(ticket.RequestedByUserId);
        var repository = Substitute.For<ISupportTicketRepository>();
        repository.GetByIdAsync(ticket.TenantId, ticket.Id, Arg.Any<CancellationToken>()).Returns(ticket);
        var handler = new AddSupportTicketCommentHandler(tenant, user, repository, bridge);
        Assert.True((await handler.Handle(new(ticket.Id, "Customer reply", key), CancellationToken.None)).IsSuccess);
        Assert.Equal(key.ToString("D"), Assert.Single(recorder.Requests).Key);
        Assert.EndsWith($"/{remote:D}/comments", recorder.LastPath);
    }

    [Fact]
    public async Task List_UsesPagedQueryRatherThanRequestPerTicket()
    {
        var tenantId = Guid.NewGuid(); var userId = Guid.NewGuid();
        var tickets = Enumerable.Range(0, 30).Select(_ => SupportTicket.Create(tenantId, userId,
            "Test subject", "Test body", SupportTicketCategory.General).Value).ToArray();
        foreach (var ticket in tickets) ticket.AttachExternalRef($"fp-support:{Guid.NewGuid()}");
        var remote = tickets[0].ExternalRef![11..];
        var recorder = new RecordingHandler($$$"""{"results":[{"id":"{{{remote}}}","number":"FP-1","status":"in_progress","commentsTruncated":true,"comments":[{"id":"public-1","body":"Public answer from agent","visibility":"public","createdAt":"2026-10-14T10:00:00Z","author":{"name":"Support"}},{"id":"private-1","body":"Private note","visibility":"internal","createdAt":"2026-10-14T10:00:00Z"}]}],"total":1,"page":1,"pageSize":20}""");
        using var recordedClient = new HttpClient(recorder);
        var bridge = new SupportPortalBridge(recordedClient, Options());
        var result = await bridge.ListAsync(tickets, CancellationToken.None);
        Assert.Equal(30, result.Count);
        Assert.Single(recorder.Requests);
        Assert.Contains("page=1&pageSize=20", recorder.LastPath);
        Assert.Contains("includeComments=1", recorder.LastPath);
        Assert.Equal("connected", result[tickets[0].Id].SyncState);
        Assert.True(result[tickets[0].Id].CommentsTruncated);
        Assert.Equal("Public answer from agent", Assert.Single(result[tickets[0].Id].Comments).Body);
    }

    [Fact]
    public void Configuration_RejectsInsecureRemoteHttp_AllowsLoopback()
    {
        Assert.Throws<InvalidOperationException>(() => Options("http://support.example"));
        Assert.Throws<InvalidOperationException>(() => Options("https://support.example/prefix"));
        Assert.True(Options("http://127.0.0.1:8000").Enabled);
        Assert.False(new SupportPortalOptions(new ConfigurationBuilder().Build()).Enabled);
    }

    private sealed class RecordingHandler(string response) : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public string LastPath { get; private set; } = "";
        public List<(string? Key, string Subject, string? Authorization, string? Body)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastPath = request.RequestUri!.PathAndQuery;
            Requests.Add((request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null,
                request.Headers.GetValues("X-Support-Subject").Single(), request.Headers.Authorization?.ToString(),
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return new(Status) { Content = new StringContent(response) };
        }
    }
}

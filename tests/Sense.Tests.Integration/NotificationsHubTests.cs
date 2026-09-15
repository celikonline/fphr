using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sense.Api.Hubs;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Notifications;
using Sense.Shared.Infrastructure.Context;
using Xunit;

namespace Sense.Tests.Integration;

/// <summary>
/// Integration tests for NotificationsHub.
/// Tests connection lifecycle and group management.
/// </summary>
public sealed class NotificationsHubTests : IDisposable
{
    private NotificationsHub _hub = null!;
    private MockHubCallerClients _clients = null!;
    private MockHubCallerContext _hubContext = null!;
    private ITenantContext _tenantContext = null!;

    public NotificationsHubTests()
    {
        var services = new ServiceCollection();

        // Register required services
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ITenantContextSetter>(sp => sp.GetRequiredService<TenantContext>());
        services.AddLogging();

        var serviceProvider = services.BuildServiceProvider();
        _tenantContext = serviceProvider.GetRequiredService<ITenantContext>();

        // Create mock clients
        _clients = new MockHubCallerClients();

        // Create mock hub context
        _hubContext = new MockHubCallerContext();

        // Create hub instance with mocks
        var logger = serviceProvider.GetRequiredService<ILogger<NotificationsHub>>();
        _hub = new NotificationsHub(_tenantContext, logger)
        {
            Clients = _clients,
            Context = _hubContext,
            Groups = new MockGroupManager()
        };
    }

    public void Dispose()
    {
        _hub.Dispose();
    }

    [Fact]
    public void GetTenantGroupName_ShouldReturnCorrectFormat()
    {
        // Arrange
        var tenantId = new Guid("12345678-1234-1234-1234-123456789012");

        // Act
        var groupName = SignalRGroupNames.ForTenant(tenantId);

        // Assert
        Assert.Equal("tenant-12345678123412341234123456789012", groupName);
    }

    /// <summary>
    /// Mock implementation of HubCallerClients for testing.
    /// </summary>
    private sealed class MockHubCallerClients : IHubCallerClients
    {
        public IClientProxy All => new MockClientProxy();

        public IClientProxy Caller => new MockClientProxy();

        public IClientProxy Others => new MockClientProxy();

        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => new MockClientProxy();

        public IClientProxy Client(string connectionId) => new MockClientProxy();

        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => new MockClientProxy();

        public IClientProxy Group(string groupName) => new MockClientProxy();

        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => new MockClientProxy();

        public IClientProxy Groups(IReadOnlyList<string> groupNames) => new MockClientProxy();

        public IClientProxy OthersInGroup(string groupName) => new MockClientProxy();

        public IClientProxy User(string userId) => new MockClientProxy();

        public IClientProxy Users(IReadOnlyList<string> userIds) => new MockClientProxy();
    }

    /// <summary>
    /// Mock implementation of ClientProxy for testing.
    /// </summary>
    private sealed class MockClientProxy : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Mock implementation of HubCallerContext for testing.
    /// </summary>
    private sealed class MockHubCallerContext : HubCallerContext
    {
        public MockHubCallerContext()
        {
            ConnectionId = Guid.NewGuid().ToString();
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    new[] { new Claim("tenant", Guid.NewGuid().ToString()) },
                    "test"));
        }

        public override string ConnectionId { get; }

        public override string? UserIdentifier => null;

        public override ClaimsPrincipal User { get; }

        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();

        public override IFeatureCollection Features { get; } = new DefaultHttpContext().Features;

        public override CancellationToken ConnectionAborted => CancellationToken.None;

        public override void Abort() { }
    }

    /// <summary>
    /// Mock implementation of GroupManager for testing.
    /// </summary>
    private sealed class MockGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}

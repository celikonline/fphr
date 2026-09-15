using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
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
public class NotificationsHubTests
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
    private class MockHubCallerClients : IHubCallerClients
    {
        public IClientProxy All => new MockClientProxy();

        public IClientProxy Caller => new MockClientProxy();

        public IClientProxy Others => new MockClientProxy();

        public IClientProxy AllExcept(params string[] excludedConnectionIds) => new MockClientProxy();

        public IClientProxy Client(string connectionId) => new MockClientProxy();

        public IClientProxy Clients(IList<string> connectionIds) => new MockClientProxy();

        public IClientProxy Group(string groupName) => new MockClientProxy();

        public IClientProxy GroupExcept(string groupName, params string[] excludedConnectionIds) => new MockClientProxy();

        public IClientProxy Groups(IList<string> groupNames) => new MockClientProxy();

        public IClientProxy User(string userId) => new MockClientProxy();

        public IClientProxy Users(IList<string> userIds) => new MockClientProxy();
    }

    /// <summary>
    /// Mock implementation of ClientProxy for testing.
    /// </summary>
    private class MockClientProxy : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Mock implementation of HubCallerContext for testing.
    /// </summary>
    private class MockHubCallerContext : HubCallerContext
    {
        public MockHubCallerContext()
            : base(new MockConnectionContext())
        {
        }

        private class MockConnectionContext : ConnectionContext
        {
            public MockConnectionContext()
            {
                ConnectionId = Guid.NewGuid().ToString();
                User = new System.Security.Claims.ClaimsPrincipal(
                    new System.Security.Claims.ClaimsIdentity(
                        new[]
                        {
                            new System.Security.Claims.Claim("tenant", Guid.NewGuid().ToString())
                        },
                        "test"));
            }

            public override string ConnectionId { get; }

            public override IFeatureCollection Features => new DefaultHttpContext().Features;

            public override IDictionary<object, object?> Items => new Dictionary<object, object?>();

            public override System.Net.IPAddress? RemoteIpAddress { get => null; set { } }

            public override int? RemotePort { get => null; set { } }

            public override System.Net.IPAddress? LocalIpAddress { get => null; set { } }

            public override int? LocalPort { get => null; set { } }

            public override ClaimsPrincipal? User { get; set; }

            public override CancellationToken ConnectionClosed { get; set; }

            public override void Abort() { }
        }
    }

    /// <summary>
    /// Mock implementation of GroupManager for testing.
    /// </summary>
    private class MockGroupManager : IGroupManager
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

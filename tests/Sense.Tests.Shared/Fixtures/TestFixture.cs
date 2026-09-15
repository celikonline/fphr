using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Respawn;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Infrastructure.Context;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Infrastructure.Messaging;
using Sense.Shared.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.Persistence.Outbox;
using Sense.Shared.Kernel.Results;
using Testcontainers.PostgreSql;
using Xunit;

namespace Sense.Tests.Shared.Fixtures;

/// <summary>
/// Reusable test fixture for unit/integration tests.
/// - Wraps PostgreSQL testcontainers for isolated DB instances
/// - Per-test isolation via Respawn
/// - Properties for DbContext, TenantContext, Dispatcher, and ServiceProvider
/// - Helper methods for tenant/user creation, command/query execution, and audit inspection
/// </summary>
[Collection("PostgreSQL")]
public abstract class TestFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _postgresContainer;
    private Respawner? _respawner;
    protected IServiceProvider? ServiceProvider { get; private set; }
    protected IDispatcher? Dispatcher { get; private set; }
    protected ITenantContextSetter? TenantContextSetter { get; private set; }
    protected ITenantContext? TenantContextAccessor { get; private set; }
    protected ICurrentUser? CurrentUser { get; private set; }
    protected TimeProvider? Clock { get; private set; }

    /// <summary>Override to configure services. Called after base services are registered.</summary>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
    }

    /// <summary>
    /// Override to run one-time async setup once the container/ServiceProvider are ready (e.g. module DbContext
    /// migrations for a test that registers a real module via <see cref="ConfigureServices"/>).
    /// </summary>
    protected virtual Task OnDatabaseReadyAsync() => Task.CompletedTask;

    /// <summary>Initialize the test container and services.</summary>
    public async ValueTask InitializeAsync()
    {
        _postgresContainer = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("test_db")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await _postgresContainer.StartAsync();
        var connectionString = _postgresContainer.GetConnectionString();

        // Build service collection with shared infrastructure
        var services = new ServiceCollection();

        // Create a minimal configuration for tests
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ConnectionStrings:Database", connectionString },
                { "ConnectionStrings:Redis", "" },
                { "Serilog:MinimumLevel", "Information" },
                { "Paging:DefaultPageSize", "50" },
                { "Paging:MaxPageSize", "1000" },
                { "Caching:Enabled", "false" },
                { "Caching:DefaultExpirationMinutes", "10" },
                { "Caching:LocalExpirationSeconds", "5" },
                { "Outbox:CheckInterval", "5000" },
            })
            .Build();

        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton(TimeProvider.System);
        Clock = TimeProvider.System;

        // LoggingBehaviour (pipeline) requires ILogger<> to be resolvable for every request type.
        services.AddLogging();

        // Register core infrastructure
        services.AddSenseCore(config);

        // Allow tests to customize services
        ConfigureServices(services);

        ServiceProvider = services.BuildServiceProvider();
        Dispatcher = ServiceProvider.GetRequiredService<IDispatcher>();
        TenantContextSetter = ServiceProvider.GetRequiredService<ITenantContextSetter>();
        TenantContextAccessor = ServiceProvider.GetRequiredService<ITenantContext>();
        CurrentUser = ServiceProvider.GetRequiredService<ICurrentUser>();

        // Runs pending module migrations (if a test's ConfigureServices registered a real module DbContext);
        // must happen before the respawner is created, otherwise there are no tables yet to track/reset.
        await OnDatabaseReadyAsync();

        // Setup respawner for database cleanup between tests. The string-based CreateAsync overload only
        // infers DbAdapter.SqlServer; Postgres requires the DbConnection overload explicitly (Respawn 6.x).
        // A fixture that never registers a module (no tables at all) leaves _respawner null; ResetDatabaseAsync
        // then no-ops instead of throwing "No tables found".
        var options = new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = new[] { "public", "identity", "org", "employee", "leave", "documents", "datatransfer", "workflow", "notifications" },
        };

        await using (var respawnerConnection = new Npgsql.NpgsqlConnection(connectionString))
        {
            await respawnerConnection.OpenAsync();
            try
            {
                _respawner = await Respawner.CreateAsync(respawnerConnection, options);
            }
            catch (InvalidOperationException)
            {
                _respawner = null;
            }
        }
    }

    /// <summary>Clean up resources.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_postgresContainer != null)
        {
            await _postgresContainer.StopAsync();
            await _postgresContainer.DisposeAsync();
        }

        (ServiceProvider as IAsyncDisposable)?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>Reset database between tests. No-ops if no module ever registered a table (nothing to reset).</summary>
    public async Task ResetDatabaseAsync()
    {
        if (_postgresContainer == null)
        {
            throw new InvalidOperationException("Test fixture not initialized.");
        }

        if (_respawner == null)
        {
            return;
        }

        var connectionString = _postgresContainer.GetConnectionString();
        await using var connection = new Npgsql.NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await _respawner.ResetAsync(connection);
    }

    /// <summary>Create a tenant in the database.</summary>
    public async Task<Guid> CreateTenantAsync(string name, string? slug = null)
    {
        if (ServiceProvider == null)
            throw new InvalidOperationException("ServiceProvider not initialized.");

        var tenantId = Guid.NewGuid();
        slug ??= name.ToLowerInvariant().Replace(" ", "-");

        // Set tenant context for creating the tenant record
        using (TenantContextSetter!.BeginScope(tenantId, slug))
        {
            // Tenant creation would go here once the Identity module supports it
            // For now, we just set the ID so tests can use it
        }

        return tenantId;
    }

    /// <summary>Create a user in the database.</summary>
    public async Task<Guid> CreateUserAsync(Guid tenantId, string email, IReadOnlySet<string>? roles = null)
    {
        if (ServiceProvider == null)
            throw new InvalidOperationException("ServiceProvider not initialized.");

        var userId = Guid.NewGuid();
        roles ??= new HashSet<string> { "User" };

        using (TenantContextSetter!.BeginScope(tenantId))
        {
            // User creation would go here once the Identity module supports it
            // For now, we just set the ID so tests can use it
        }

        return userId;
    }

    /// <summary>Execute a command and return the result.</summary>
    public async Task<Result> ExecuteCommandAsync(ICommand command)
    {
        if (Dispatcher == null)
            throw new InvalidOperationException("Dispatcher not initialized.");

        return await Dispatcher.Send(command);
    }

    /// <summary>Execute a command with a result.</summary>
    public async Task<Result<T>> ExecuteCommandAsync<T>(ICommand<T> command)
    {
        if (Dispatcher == null)
            throw new InvalidOperationException("Dispatcher not initialized.");

        return await Dispatcher.Send(command);
    }

    /// <summary>Execute a query and return the result.</summary>
    public async Task<Result<T>> ExecuteQueryAsync<T>(IQuery<T> query)
    {
        if (Dispatcher == null)
            throw new InvalidOperationException("Dispatcher not initialized.");

        return await Dispatcher.Query(query);
    }

    /// <summary>Get audit outbox messages for a tenant.</summary>
    public async Task<List<OutboxMessage>> GetAuditEventsAsync(Guid tenantId)
    {
        if (ServiceProvider == null)
            throw new InvalidOperationException("ServiceProvider not initialized.");

        // This would query the outbox table across all module contexts
        // Implementation depends on how we structure multi-context access
        return new List<OutboxMessage>();
    }

    /// <summary>Set the current user context for tests.</summary>
    public void SetCurrentUser(Guid tenantId, Guid userId, string email, IReadOnlySet<string> roles)
    {
        if (CurrentUser is not CurrentUserAccessor accessor)
            throw new InvalidOperationException("CurrentUser is not a CurrentUserAccessor.");

        var user = new AuthenticatedUser(
            UserId: userId,
            EmployeeId: null,
            Email: email,
            DisplayName: email.Split('@')[0],
            Roles: roles,
            IsPlatformAdmin: false,
            ImpersonatorUserId: null,
            ApiKeyId: null,
            CorrelationId: Guid.NewGuid().ToString(),
            IpAddress: "127.0.0.1");

        accessor.Set(user);
        TenantContextSetter?.BeginScope(tenantId);
    }

    /// <summary>Set system user context (no tenant requirement).</summary>
    public void SetSystemUser()
    {
        if (CurrentUser is not CurrentUserAccessor accessor)
            throw new InvalidOperationException("CurrentUser is not a CurrentUserAccessor.");

        accessor.Set(SystemUser.Instance);
    }
}

/// <summary>Collection definition for PostgreSQL tests to ensure sequential execution per container.</summary>
[CollectionDefinition("PostgreSQL", DisableParallelization = true)]
public class PostgreSqlCollection : ICollectionFixture<TestFixture>;

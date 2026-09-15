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

        // Setup respawner for database cleanup between tests
        var options = new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = new[] { "public", "identity", "org", "employee", "leave", "documents" },
        };

        // Respawn 6.x's string-connection overload only supports SqlAdapter.SqlServer regardless of
        // options.DbAdapter; a non-SqlServer adapter (Postgres here) requires the DbConnection overload.
        await using (var setupConnection = new Npgsql.NpgsqlConnection(connectionString))
        {
            await setupConnection.OpenAsync();

            // Respawner.CreateAsync inspects existing tables and throws "No tables found" against a
            // brand-new container (before any module has run its migrations, which happens later,
            // per-test, if at all). A harmless bootstrap table guarantees at least one table exists so
            // construction succeeds regardless of whether/when a test applies real migrations; Respawn
            // resetting it between tests is a no-op since nothing ever writes to it.
            await using (var bootstrapCommand = new Npgsql.NpgsqlCommand(
                "CREATE TABLE IF NOT EXISTS public.__respawn_bootstrap (id int)", setupConnection))
            {
                await bootstrapCommand.ExecuteNonQueryAsync();
            }

            _respawner = await Respawner.CreateAsync(setupConnection, options);
        }

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

        // Register core infrastructure
        services.AddSenseCore(config);

        // Allow tests to customize services
        ConfigureServices(services);

        ServiceProvider = services.BuildServiceProvider();
        Dispatcher = ServiceProvider.GetRequiredService<IDispatcher>();
        TenantContextSetter = ServiceProvider.GetRequiredService<ITenantContextSetter>();
        TenantContextAccessor = ServiceProvider.GetRequiredService<ITenantContext>();
        CurrentUser = ServiceProvider.GetRequiredService<ICurrentUser>();
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

    /// <summary>Reset database between tests.</summary>
    public async Task ResetDatabaseAsync()
    {
        if (_respawner == null || _postgresContainer == null)
        {
            throw new InvalidOperationException("Test fixture not initialized.");
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

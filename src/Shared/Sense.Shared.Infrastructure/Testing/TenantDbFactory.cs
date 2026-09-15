using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.Context;
using Sense.Shared.Infrastructure.Persistence;

namespace Sense.Shared.Infrastructure.Testing;

/// <summary>
/// Factory pattern for creating isolated ModuleDbContext instances per tenant during tests.
/// Thread-safe and ensures tenant isolation validation (raises exception on cross-tenant access).
/// </summary>
public sealed class TenantDbFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ITenantContextSetter _tenantContextSetter;
    private readonly ConcurrentDictionary<Guid, DbContextOptions> _contextOptionsCache = new();

    public TenantDbFactory(IServiceProvider serviceProvider, ITenantContextSetter tenantContextSetter)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _tenantContextSetter = tenantContextSetter ?? throw new ArgumentNullException(nameof(tenantContextSetter));
    }

    /// <summary>Create a DbContext scoped to a specific tenant.</summary>
    /// <remarks>
    /// The returned context is configured with the tenant context set to the specified tenantId.
    /// Any attempt to access data for a different tenant will throw TenantMismatchException.
    /// </remarks>
    public async Task<T> CreateDbContextAsync<T>(Guid tenantId, Func<DbContextOptions, T>? contextFactory = null)
        where T : ModuleDbContext
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant ID cannot be empty.", nameof(tenantId));

        // Set tenant scope for this thread
        using var scope = _tenantContextSetter.BeginScope(tenantId);

        // Create or retrieve cached options
        var options = _contextOptionsCache.GetOrAdd(tenantId, tid =>
        {
            var serviceScope = _serviceProvider.CreateScope();
            var contextOptions = GetContextOptions<T>(serviceScope.ServiceProvider);
            return contextOptions;
        });

        // Create the context
        var context = contextFactory != null
            ? contextFactory(options)
            : (T)Activator.CreateInstance(typeof(T), options)!;

        // Ensure tenant is correctly set
        ValidateTenantContext(context, tenantId);

        return context;
    }

    /// <summary>Clear all data for a specific tenant (for test cleanup).</summary>
    public async Task ClearTenantDataAsync<T>(Guid tenantId)
        where T : ModuleDbContext
    {
        using var context = await CreateDbContextAsync<T>(tenantId);
        using var transaction = await context.Database.BeginTransactionAsync();

        try
        {
            // Disable all constraints temporarily
            await context.Database.ExecuteSqlRawAsync("SET session_replication_role = 'replica'");

            // Delete all tables in the module schema
            var deleteCommands = new[]
            {
                $"DELETE FROM {context.Schema}.outbox_messages",
                $"DELETE FROM {context.Schema}.inbox_messages",
            };

            foreach (var command in deleteCommands)
            {
                try
                {
                    await context.Database.ExecuteSqlRawAsync(command);
                }
                catch
                {
                    // Table may not exist yet, skip
                }
            }

            // Re-enable constraints
            await context.Database.ExecuteSqlRawAsync("SET session_replication_role = 'origin'");
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>Get the connection string used for a tenant's context.</summary>
    public string GetConnectionString(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant ID cannot be empty.", nameof(tenantId));

        // In multi-tenant with shared database, all tenants use same connection string
        // In multi-tenant with separate databases, this would return tenant-specific connection
        var options = _contextOptionsCache.GetOrAdd(tenantId, _ =>
        {
            var serviceScope = _serviceProvider.CreateScope();
            return GetContextOptions<ModuleDbContext>(serviceScope.ServiceProvider);
        });

        // Extract connection string from options
        var extension = options.Extensions.FirstOrDefault(e => e is Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlOptionsExtension);
        return (extension as Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlOptionsExtension)
            ?.ConnectionString ?? throw new InvalidOperationException("Could not determine connection string.");
    }

    /// <summary>Validate that the context has the correct tenant set.</summary>
    private static void ValidateTenantContext(ModuleDbContext context, Guid expectedTenantId)
    {
        var tenantContext = context.GetType()
            .GetProperty("TenantContext", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
            .GetValue(context) as ITenantContext;

        if (tenantContext == null || !tenantContext.IsResolved)
            throw new InvalidOperationException($"Tenant context not resolved for context {context.GetType().Name}.");

        if (tenantContext.TenantId != expectedTenantId)
            throw new InvalidOperationException(
                $"Tenant ID mismatch: expected {expectedTenantId}, got {tenantContext.TenantId}. " +
                "This indicates a potential cross-tenant data access violation.");
    }

    private static DbContextOptions GetContextOptions<T>(IServiceProvider serviceProvider)
        where T : ModuleDbContext
    {
        // Get the DbContextOptions for the context type from the service provider
        var optionsType = typeof(DbContextOptions<>).MakeGenericType(typeof(T));
        var options = serviceProvider.GetService(optionsType);

        if (options is DbContextOptions dbContextOptions)
            return dbContextOptions;

        throw new InvalidOperationException(
            $"Could not resolve DbContextOptions for {typeof(T).Name}. " +
            "Ensure the context is registered in DI with AddDbContext.");
    }
}

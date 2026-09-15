using System.Reflection;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Minio;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Persistence;
using Sense.Shared.Contracts.Storage;
using Sense.Shared.Infrastructure.Context;
using Sense.Shared.Infrastructure.Events;
using Sense.Shared.Infrastructure.Messaging;
using Sense.Shared.Infrastructure.Messaging.Behaviours;
using Sense.Shared.Infrastructure.Observability;
using Sense.Shared.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.Persistence.Outbox;
using Sense.Shared.Infrastructure.Storage;

namespace Sense.Shared.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    private const string MigrationsHistoryTable = "__ef_migrations_history";
    private const int DbRetryCount = 3;
    private const string DatabaseConnectionMissing = "ConnectionStrings:Database is not configured.";

    /// <summary>Tüm host'ların (Api, Worker, Migrator) ortak altyapısı: ayarlar, bağlam, dispatcher, pipeline, cache, event bus.</summary>
    public static IServiceCollection AddSenseCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PagingOptions>().Bind(configuration.GetSection(ConfigurationSections.Paging)).ValidateOnStart();
        services.AddOptions<LocalizationOptions>().Bind(configuration.GetSection(ConfigurationSections.Localization)).ValidateOnStart();
        services.AddOptions<CachingOptions>().Bind(configuration.GetSection(ConfigurationSections.Caching)).ValidateOnStart();
        services.AddOptions<OutboxOptions>().Bind(configuration.GetSection(ConfigurationSections.Outbox)).ValidateOnStart();
        services.AddOptions<DiagnosticsOptions>().Bind(configuration.GetSection(ConfigurationSections.Diagnostics)).ValidateOnStart();
        services.AddOptions<ProblemMappingOptions>().Bind(configuration.GetSection(ConfigurationSections.ProblemDetails)).ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);

        services.TryAddScoped<TenantContext>();
        services.TryAddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.TryAddScoped<ITenantContextSetter>(sp => sp.GetRequiredService<TenantContext>());

        services.TryAddScoped<CurrentUserAccessor>();
        services.TryAddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUserAccessor>());

        services.TryAddScoped<CorrelationIdContext>();
        services.TryAddScoped<ICorrelationIdContext>(sp => sp.GetRequiredService<CorrelationIdContext>());
        services.TryAddScoped<ICorrelationIdContextSetter>(sp => sp.GetRequiredService<CorrelationIdContext>());

        services.TryAddScoped<IDispatcher, Dispatcher>();
        services.TryAddScoped<IModuleUnitOfWorkResolver, ModuleUnitOfWorkResolver>();

        // Pipeline sırası = kayıt sırası (dıştan içe).
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IPipelineBehaviour<,>), typeof(LoggingBehaviour<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IPipelineBehaviour<,>), typeof(TracingBehaviour<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IPipelineBehaviour<,>), typeof(ValidationBehaviour<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IPipelineBehaviour<,>), typeof(AuthorizationBehaviour<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IPipelineBehaviour<,>), typeof(AccessSettingsAuthorizationBehaviour<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IPipelineBehaviour<,>), typeof(UnitOfWorkBehaviour<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IPipelineBehaviour<,>), typeof(CachingBehaviour<,>)));

        services.TryAddSingleton<IEventBus, InProcessEventBus>();
        services.TryAddScoped<AuditTenantInterceptor>();

        var redis = configuration.GetConnectionString(ConnectionStringNames.Redis);
        if (!string.IsNullOrWhiteSpace(redis))
        {
            services.AddStackExchangeRedisCache(o => o.Configuration = redis);
        }

        services.AddHybridCache();
        services.AddOptions<Microsoft.Extensions.Caching.Hybrid.HybridCacheOptions>()
            .Configure<IOptions<CachingOptions>>((o, caching) =>
            {
                o.DefaultEntryOptions = new Microsoft.Extensions.Caching.Hybrid.HybridCacheEntryOptions
                {
                    Expiration = TimeSpan.FromMinutes(caching.Value.DefaultExpirationMinutes),
                    LocalCacheExpiration = TimeSpan.FromSeconds(caching.Value.LocalExpirationSeconds),
                };
            });

        return services;
    }

    /// <summary>Bir modülün handler, validator, event handler ve UnitOfWork kayıtları (assembly taraması, Scrutor).</summary>
    public static IServiceCollection AddModuleHandlers(this IServiceCollection services, string moduleName, params Assembly[] assemblies)
    {
        services.Scan(scan => scan
            .FromAssemblies(assemblies)
            .AddClasses(c => c.AssignableTo(typeof(ICommandHandler<>)), publicOnly: false).AsImplementedInterfaces().WithScopedLifetime()
            .AddClasses(c => c.AssignableTo(typeof(ICommandHandler<,>)), publicOnly: false).AsImplementedInterfaces().WithScopedLifetime()
            .AddClasses(c => c.AssignableTo(typeof(IQueryHandler<,>)), publicOnly: false).AsImplementedInterfaces().WithScopedLifetime()
            .AddClasses(c => c.AssignableTo(typeof(IIntegrationEventHandler<>)), publicOnly: false).AsImplementedInterfaces().WithScopedLifetime()
            .AddClasses(c => c.AssignableTo(typeof(IDomainEventHandler<>)), publicOnly: false).AsImplementedInterfaces().WithScopedLifetime());

        services.AddValidatorsFromAssemblies(assemblies, ServiceLifetime.Scoped, includeInternalTypes: true);

        foreach (var asm in assemblies)
        {
            ModuleUnitOfWorkResolver.Register(asm, moduleName);
        }

        EventTypeRegistry.RegisterAll(assemblies, typeof(Kernel.Domain.IDomainEvent));
        EventTypeRegistry.RegisterAll(assemblies, typeof(IIntegrationEvent));
        return services;
    }

    /// <summary>Modül DbContext'i: Npgsql + snake_case + interceptor; şema başına migration geçmişi.</summary>
    public static IServiceCollection AddModuleDbContext<TContext>(this IServiceCollection services, IConfiguration configuration, string schema)
        where TContext : ModuleDbContext
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringNames.Database)
            ?? throw new InvalidOperationException(DatabaseConnectionMissing);

        services.AddDbContext<TContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable(MigrationsHistoryTable, schema);
                npgsql.EnableRetryOnFailure(DbRetryCount);
            });
            options.UseSnakeCaseNamingConvention();
            options.AddInterceptors(sp.GetRequiredService<AuditTenantInterceptor>());
        });

        services.AddScoped<IModuleUnitOfWork>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped<ModuleDbContext>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped<OutboxProcessor<TContext>>();
        return services;
    }

    /// <summary>File storage with MinIO adapter and presigned URLs.</summary>
    public static IServiceCollection AddFileStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MinioOptions>().Bind(configuration.GetSection(MinioOptions.Section)).ValidateOnStart();

        var minioOptions = configuration.GetSection(MinioOptions.Section).Get<MinioOptions>();
        if (minioOptions is null || string.IsNullOrWhiteSpace(minioOptions.Endpoint))
        {
            throw new InvalidOperationException("MinIO configuration (MinIO:Endpoint, MinIO:AccessKey, MinIO:SecretKey) is required.");
        }

        // Register MinIO client
        services.AddSingleton<IMinioClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MinioOptions>>().Value;
            return new MinioClient()
                .WithEndpoint(options.Endpoint)
                .WithCredentials(options.AccessKey, options.SecretKey)
                .WithSSL(options.UseHttps)
                .Build();
        });

        // Register file storage adapter
        services.AddSingleton<IFileStorage, MinioAdapter>();

        return services;
    }
}

/// <summary>İstek tipinin assembly'sinden modül adını, oradan da modülün UnitOfWork'ünü bulur.</summary>
public sealed class ModuleUnitOfWorkResolver(IServiceProvider provider) : IModuleUnitOfWorkResolver
{
    private static readonly Dictionary<Assembly, string> ModuleByAssembly = [];

    public static void Register(Assembly assembly, string moduleName) => ModuleByAssembly[assembly] = moduleName;

    public IUnitOfWork? Resolve(Type requestType)
    {
        if (!ModuleByAssembly.TryGetValue(requestType.Assembly, out var module))
        {
            return null;
        }

        return provider.GetServices<IModuleUnitOfWork>()
            .FirstOrDefault(u => string.Equals(u.ModuleName, module, StringComparison.OrdinalIgnoreCase));
    }
}

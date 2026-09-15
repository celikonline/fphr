using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Asp.Versioning;
using FluentValidation;
using Hangfire;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Sense.Api.Hubs;
using Sense.Api.SeedData;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Notifications;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Infrastructure.Notifications;
using Sense.Shared.Web.Authorization;
using Sense.Shared.Web.ErrorHandling;
using Sense.Shared.Web.Localization;

namespace Sense.Shared.Web.DependencyInjection;

public static class WebServiceCollectionExtensions
{
    private const string OpenApiDocumentName = "v1";
    private const string ApiVersionGroupFormat = "'v'VVV";

    /// <summary>
    /// API host'u için ortak kayıt: controller'lar (her modülün Api assembly'si application part olarak eklenir),
    /// sürümleme, yerelleştirme, ProblemDetails, izin policy'leri, OpenAPI, health.
    /// </summary>
    public static IServiceCollection AddSenseWeb(this IServiceCollection services, IConfiguration configuration, IReadOnlyList<IModule> modules)
    {
        services.AddSenseCore(configuration);

        // Register seeding options
        services.AddOptions<SeedingOptions>().Bind(configuration.GetSection(SeedingOptions.Section)).ValidateOnStart();

        var localization = configuration.GetSection(ConfigurationSections.Localization).Get<LocalizationOptions>() ?? new LocalizationOptions();
        services.AddLocalization(o => o.ResourcesPath = localization.ResourcesPath);
        services.TryAddSingleton<IErrorLocalizer, ErrorLocalizer>();
        services.Configure<RequestLocalizationOptions>(o =>
        {
            var cultures = localization.SupportedCultures.Select(c => new CultureInfo(c)).ToList();
            o.DefaultRequestCulture = new RequestCulture(localization.DefaultCulture);
            o.SupportedCultures = cultures;
            o.SupportedUICultures = cultures;
            o.ApplyCurrentCultureToResponseHeaders = true;
        });
        ValidatorOptions.Global.LanguageManager.Enabled = true;

        var mvc = services.AddControllers(options =>
            {
                options.ModelBinderProviders.Insert(0, new Binding.PagedQueryBinderProvider());
                options.Filters.Add(new ProducesResponseTypeAttribute(typeof(ProblemDetails), StatusCodes.Status400BadRequest));
                options.Filters.Add(new ProducesResponseTypeAttribute(typeof(ProblemDetails), StatusCodes.Status401Unauthorized));
                options.Filters.Add(new ProducesResponseTypeAttribute(typeof(ProblemDetails), StatusCodes.Status403Forbidden));
                options.Filters.Add(new ProducesResponseTypeAttribute(typeof(ProblemDetails), StatusCodes.Status404NotFound));
            })
            .AddJsonOptions(o =>
            {
                o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
                o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            });

        foreach (var module in modules)
        {
            foreach (var asm in module.Assemblies.Distinct())
            {
                mvc.PartManager.ApplicationParts.Add(new AssemblyPart(asm));
            }

            module.AddModule(services, configuration);
        }

        services.AddSingleton(modules);

        services.AddApiVersioning(o =>
            {
                o.DefaultApiVersion = ApiVersionParser.Default.Parse(Controllers.ApiRoutes.DefaultVersion);
                o.AssumeDefaultVersionWhenUnspecified = true;
                o.ReportApiVersions = true;
                o.ApiVersionReader = new UrlSegmentApiVersionReader();
            })
            .AddMvc()
            .AddApiExplorer(o =>
            {
                o.GroupNameFormat = ApiVersionGroupFormat;
                o.SubstituteApiVersionInUrl = true;
            });

        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.TryAddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizationHandler, PermissionHandler>());
        services.AddAuthorization();

        services.AddOpenApi(OpenApiDocumentName);
        services.AddHealthChecks();
        services.AddHttpContextAccessor();

        // Configure SignalR with Redis backplane
        var redisConnection = configuration.GetConnectionString(ConnectionStringNames.Redis);
        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            services.AddSignalR()
                .AddStackExchangeRedis(redisConnection, options =>
                {
                    options.Configuration.ChannelPrefix = "senseik-signalr-";
                });
        }
        else
        {
            // Fallback to in-memory SignalR if Redis is not configured
            services.AddSignalR();
        }

        // Register notification service
        services.TryAddScoped<INotificationService, SignalRNotificationService>();

        return services;
    }

    /// <summary>
    /// Kültür seçimi: Accept-Language; ileride kullanıcı tercihi (JWT lang claim) öncelikli olacak.
    /// FluentValidation alan adları da "field.{snake_case}" kaynak anahtarlarından çevrilir; anahtar yoksa varsayılan ad kullanılır.
    /// </summary>
    public static IApplicationBuilder UseSenseLocalization(this IApplicationBuilder app)
    {
        var localizer = app.ApplicationServices.GetRequiredService<Microsoft.Extensions.Localization.IStringLocalizer<SharedResource>>();
        ValidatorOptions.Global.DisplayNameResolver = (_, member, _) =>
        {
            if (member is null)
            {
                return null;
            }

            var value = localizer[ValidationFieldKeys.For(member.Name)];
            return value.ResourceNotFound ? null : value.Value;
        };

        var options = app.ApplicationServices.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;
        return app.UseRequestLocalization(options);
    }
}

/// <summary>Doğrulama mesajlarındaki alan adlarının kaynak anahtarları: field.{snake_case}.</summary>
public static class ValidationFieldKeys
{
    public const string Prefix = "field.";

    public static string For(string propertyName) => Prefix + PropertyNames.ToSnakeCase(propertyName);
}

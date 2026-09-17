using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Asp.Versioning;
using FluentValidation;
using Hangfire;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Notifications;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Infrastructure.Notifications;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Web.Authorization;
using Sense.Shared.Web.Controllers;
using Sense.Shared.Web.ErrorHandling;
using Sense.Shared.Web.Localization;

namespace Sense.Shared.Web.DependencyInjection;

public static class WebServiceCollectionExtensions
{
    private const string OpenApiDocumentName = "v1";
    private const string ApiVersionGroupFormat = "'v'VVV";

    /// <summary>Name of the CORS policy registered in <see cref="AddSenseWeb"/> and applied via <see cref="UseSenseCors"/>.</summary>
    public const string CorsPolicyName = "SenseFrontend";

    /// <summary>
    /// API host'u için ortak kayıt: controller'lar (her modülün Api assembly'si application part olarak eklenir),
    /// sürümleme, yerelleştirme, ProblemDetails, izin policy'leri, OpenAPI, health.
    /// </summary>
    public static IServiceCollection AddSenseWeb(this IServiceCollection services, IConfiguration configuration, IReadOnlyList<IModule> modules)
    {
        services.AddSenseCore(configuration);

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

        // The SPA (web/) is served from its own origin (nginx :8080 in containers, Vite :3000 on
        // host) and calls this API cross-origin, so without a CORS policy the browser blocks every
        // request before it reaches the server - unlike curl/Postman, which don't enforce CORS.
        // Frontend:BaseUrl is already configured per-environment for email links; reuse it here too.
        var frontendBaseUrl = configuration.GetSection(ConfigurationSections.Frontend)["BaseUrl"];
        services.AddCors(options =>
        {
            options.AddPolicy(CorsPolicyName, policy =>
            {
                if (!string.IsNullOrWhiteSpace(frontendBaseUrl))
                {
                    // Exposed headers: tarayıcı JS'i X-Correlation-Id / X-Trace-Id gibi başlıkları ancak böyle okuyabilir
                    // (hata ekranında "destek kodu" göstermek, Content-Disposition'dan dosya adı almak vb.).
                    policy.WithOrigins(frontendBaseUrl)
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        // Every authenticated call carries an Authorization header, so each one is a
                        // preflighted request. Without a max-age the browser re-sends OPTIONS before
                        // *every* GET, doubling the round trips on each screen. Two hours is Chrome's
                        // ceiling; longer values are silently clamped rather than honoured.
                        .SetPreflightMaxAge(TimeSpan.FromHours(2))
                        .WithExposedHeaders(Sense.Shared.Contracts.Security.SenseHeaderNames.ExposedToClients);
                }
            });
        });

        services.TryAddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizationHandler, PermissionHandler>());
        services.AddAuthorization();

        services.AddSenseRateLimiting(configuration);

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
                    options.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("senseik-signalr-");
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

    /// <summary>Applies the CORS policy registered by <see cref="AddSenseWeb"/>. Must run before UseAuthentication/UseAuthorization.</summary>
    public static IApplicationBuilder UseSenseCors(this IApplicationBuilder app) => app.UseCors(CorsPolicyName);

    /// <summary>
    /// ADR-0003: anonim uçlar için IP bazlı sabit pencere hız sınırlama politikaları
    /// (<see cref="RateLimitPolicyNames.PublicAnonymous"/>/<see cref="RateLimitPolicyNames.PublicAnonymousWrite"/>).
    /// Politikalar yalnızca ilgili modül controller/action'larına <c>[EnableRateLimiting("...")]</c> ile uygulandığında
    /// devreye girer; global bir varsayılan limiter eklenmez (kimlik doğrulamalı uçlar etkilenmez).
    /// </summary>
    private static IServiceCollection AddSenseRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(ConfigurationSections.RateLimiting).Get<RateLimitingOptions>() ?? new RateLimitingOptions();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(RateLimitPolicyNames.PublicAnonymous, httpContext => CreatePartition(httpContext, options.PublicAnonymous));
            limiter.AddPolicy(RateLimitPolicyNames.PublicAnonymousWrite, httpContext => CreatePartition(httpContext, options.PublicAnonymousWrite));
            limiter.OnRejected = WriteRateLimitProblemAsync;
        });

        return services;
    }

    /// <summary>IP adresine göre bölümlenmiş sabit pencere limiter; ters proxy arkasında X-Forwarded-For zaten <see cref="HttpContext.Connection"/>'a yansıtılmış olmalı (bkz. Forwarded Headers middleware'i).</summary>
    private static RateLimitPartition<string> CreatePartition(HttpContext httpContext, RateLimitPolicyOptions policyOptions)
    {
        var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = policyOptions.PermitLimit,
            Window = TimeSpan.FromSeconds(policyOptions.WindowSeconds),
            QueueLimit = policyOptions.QueueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        });
    }

    /// <summary>Diğer hatalarla aynı ProblemDetails biçimi (bkz. <see cref="ErrorHandling.GlobalExceptionHandler"/>); Retry-After başlığı eklenir.</summary>
    private static async ValueTask WriteRateLimitProblemAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var httpContext = context.HttpContext;
        var localizer = httpContext.RequestServices.GetRequiredService<IErrorLocalizer>();
        var problemOptions = httpContext.RequestServices.GetRequiredService<IOptions<ProblemMappingOptions>>().Value;
        var error = new Error(ErrorCodes.RateLimitExceeded, ErrorType.TooManyRequests);

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            httpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = localizer.Title(ErrorType.TooManyRequests),
            Detail = localizer.Message(error),
            Type = problemOptions.TypeBaseUrl + error.Code,
            Instance = httpContext.Request.Path,
        };
        problem.Extensions[Sense.Shared.Contracts.Configuration.ProblemDetailsDefaults.CodeExtension] = error.Code;
        problem.Extensions[Sense.Shared.Contracts.Configuration.ProblemDetailsDefaults.TraceIdExtension] = httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        httpContext.Response.ContentType = MediaTypes.ProblemJson;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Doğrulama mesajlarındaki alan adlarının kaynak anahtarları: field.{snake_case}.</summary>
public static class ValidationFieldKeys
{
    public const string Prefix = "field.";

    public static string For(string propertyName) => Prefix + PropertyNames.ToSnakeCase(propertyName);
}

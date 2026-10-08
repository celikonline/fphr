using Hangfire;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using Sense.Api;
using Sense.Api.HealthChecks;
using Sense.Api.Middleware;
using Sense.Api.Observability;
using Sense.Api.SeedData;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Infrastructure.Notifications;
using Sense.Shared.Infrastructure.Observability;
using Sense.Shared.Web.DependencyInjection;
using Sense.Shared.Web.Middleware;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // DSN yoksa (varsayılan local dev durumu) no-op'tur; build()'den önce çağrılmalı.
    builder.AddSenseSentry();

    builder.Host.UseSerilog((context, services, cfg) => cfg
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.With<TenantEnricher>()
        .Enrich.With<CorrelationIdEnricher>()
        .Enrich.With(new UserEnricher(services.GetRequiredService<IHttpContextAccessor>()))
        .Enrich.WithProperty(HostConstants.ApplicationProperty, context.Configuration[HostConstants.ApplicationNameKey] ?? HostConstants.DefaultApplicationName)
        .WriteToSenseLogExport(context.Configuration, context.HostingEnvironment));

    // "Server: Kestrel" başlığı sunucu teknolojisini ifşa etmesin.
    builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);

    var observabilityOptions = builder.Configuration.GetSection(ConfigurationSections.Observability).Get<ObservabilityOptions>()
        ?? new ObservabilityOptions();

    var modules = ModuleCatalog.Modules;
    builder.Services.AddSenseWeb(builder.Configuration, modules);
    builder.Services.AddFileStorage(builder.Configuration);
    builder.Services.AddSenseAuthentication(builder.Configuration);
    builder.Services.AddHangfireServices(builder.Configuration);
    builder.Services.AddDataSeeding();
    builder.Services.AddOptions<SeedingOptions>().Bind(builder.Configuration.GetSection(SeedingOptions.Section)).ValidateOnStart();
    builder.Services.AddSenseObservability(builder.Configuration, builder.Environment);
    builder.Services.AddSenseHealthChecks(builder.Configuration);

    var app = builder.Build();

    // En erken middleware: sonraki her şey (exception handler dahil) aynı correlation id'yi görsün.
    app.UseCorrelationId();
    app.UseClientContext();
    app.UseSecurityHeaders();

    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
    }

    app.UseExceptionHandler();
    app.UseSerilogRequestLogging(o =>
    {
        // Health check polling'i ve Prometheus scrape'i log gürültüsü yaratmasın.
        o.GetLevel = (httpContext, elapsedMs, ex) => ex is not null
            ? LogEventLevel.Error
            : OpenTelemetryExtensions.IsInfrastructureRequest(httpContext.Request.Path, observabilityOptions)
                ? LogEventLevel.Verbose
                : httpContext.Response.StatusCode >= StatusCodes.Status500InternalServerError
                    ? LogEventLevel.Error
                    : elapsedMs > 1000
                        ? LogEventLevel.Warning
                        : LogEventLevel.Information;
    });
    app.UseHttpsRedirection();
    app.UseSenseLocalization();
    app.UseSenseCors();

    // ADR-0003: anonim uçlar için IP bazlı hız sınırlama (public-anonymous / public-anonymous-write);
    // yalnızca [EnableRateLimiting("...")] ile işaretli uç noktalarda devreye girer.
    app.UseRateLimiter();

    app.UseAuthentication();
    app.UseRequestContext();
    app.UseMiddleware<CommercialLicenseMiddleware>();
    app.UseAuthorization();

    app.MapControllers();
    app.MapSenseHealthChecks();
    app.MapSenseMetrics(app.Configuration);
    app.MapOpenApi(HostConstants.OpenApiRoutePattern);
    app.MapScalarApiReference(HostConstants.ScalarPath, o => o.WithTitle(HostConstants.ApiTitle));

    // Map SignalR hubs
    app.MapHub<NotificationsHub>("/hubs/notifications");

    // Map Hangfire dashboard (admin role protection would be added here)
    app.UseHangfireDashboard(options: new DashboardOptions
    {
        IgnoreAntiforgeryToken = true,
        DashboardTitle = "Sense.İK Background Jobs"
    });

    // Seed data if enabled
    var seedingOptions = app.Services.GetRequiredService<IOptions<SeedingOptions>>().Value;
    if (seedingOptions.Enabled)
    {
        using var scope = app.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
        await seeder.SeedAsync(seedingOptions.Profile);
    }

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, HostConstants.StartupFailedMessage);
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>WebApplicationFactory için görünür giriş noktası.</summary>
public partial class Program;

using Hangfire;
using Scalar.AspNetCore;
using Sense.Api;
using Sense.Api.Hubs;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Web.DependencyInjection;
using Sense.Shared.Web.Middleware;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, cfg) => cfg
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty(HostConstants.ApplicationProperty, context.Configuration[HostConstants.ApplicationNameKey] ?? HostConstants.DefaultApplicationName));

    var modules = ModuleCatalog.Modules;
    builder.Services.AddSenseWeb(builder.Configuration, modules);
    builder.Services.AddFileStorage(builder.Configuration);
    builder.Services.AddSenseAuthentication(builder.Configuration);
    builder.Services.AddHangfireServices(builder.Configuration);

    var app = builder.Build();

    app.UseExceptionHandler();
    app.UseSerilogRequestLogging();
    app.UseHttpsRedirection();
    app.UseSenseLocalization();

    app.UseAuthentication();
    app.UseRequestContext();
    app.UseAuthorization();

    app.MapControllers();
    app.MapHealthChecks(HostConstants.HealthPath);
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

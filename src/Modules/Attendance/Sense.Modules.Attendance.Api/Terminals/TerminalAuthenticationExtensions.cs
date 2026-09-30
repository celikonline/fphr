using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Sense.Modules.Attendance.Api.Terminals;

public static class TerminalAuthenticationExtensions
{
    /// <summary>
    /// Okutma ucu için API kaydı: <see cref="TerminalKeyDefaults.AuthenticationScheme"/> şeması (varsayılan şema DEĞİL - JWT varsayılan kalır;
    /// yalnız [Authorize(AuthenticationSchemes = ...)] ile istenir), IP başına başarısız deneme kovası ve terminal başına istek bütçesi.
    /// </summary>
    public static IServiceCollection AddTerminalIngestEndpointSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, TerminalKeyAuthenticationHandler>(TerminalKeyDefaults.AuthenticationScheme, displayName: null, _ => { });
        services.AddOptions<TerminalIngestRateLimitOptions>().Bind(configuration.GetSection(TerminalIngestRateLimitOptions.Section));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<TerminalAuthFailureTracker>();
        services.TryAddSingleton<TerminalRequestThrottle>();
        return services;
    }
}

using Microsoft.AspNetCore.Authentication;

namespace Sense.Modules.Attendance.Api.Terminals;

public static class TerminalAuthenticationExtensions
{
    /// <summary>
    /// Okutma ucu için API kaydı: <see cref="TerminalKeyDefaults.AuthenticationScheme"/> şeması (varsayılan şema DEĞİL - JWT varsayılan kalır;
    /// yalnız [Authorize(AuthenticationSchemes = ...)] ile istenir) ve <see cref="TerminalKeyDefaults.RateLimitPolicy"/> politikası.
    /// </summary>
    public static IServiceCollection AddTerminalIngestEndpointSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, TerminalKeyAuthenticationHandler>(TerminalKeyDefaults.AuthenticationScheme, displayName: null, _ => { });
        services.AddTerminalIngestRateLimiting(configuration);
        return services;
    }
}

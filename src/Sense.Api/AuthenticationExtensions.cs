using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Security;

namespace Sense.Api;

/// <summary>JWT ayarları (appsettings "Auth").</summary>
public sealed class AuthOptions
{
    public string Issuer { get; set; } = AuthDefaults.Issuer;

    public string Audience { get; set; } = AuthDefaults.Audience;

    public string KeyId { get; set; } = AuthDefaults.KeyId;

    /// <summary>RSA özel anahtar (PEM). Lokal: user-secrets; prod: Key Vault. Boşsa geçici anahtar üretilir (yalnız geliştirme).</summary>
    public string? SigningKeyPem { get; set; }

    public int ClockSkewSeconds { get; set; } = AuthDefaults.ClockSkewSeconds;

    public string HubPathPrefix { get; set; } = AuthDefaults.HubPathPrefix;
}

public static class AuthDefaults
{
    public const string Issuer = "senseik";
    public const string Audience = "senseik-api";
    public const string KeyId = "sense-dev";
    public const int ClockSkewSeconds = 30;
    public const string HubPathPrefix = "/hubs";
    public const int DevKeySize = 2048;
    public const string AccessTokenQueryParameter = "access_token";
}

public static class AuthenticationExtensions
{
    public static IServiceCollection AddSenseAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var auth = configuration.GetSection(ConfigurationSections.Auth).Get<AuthOptions>() ?? new AuthOptions();
        services.AddOptions<AuthOptions>().Bind(configuration.GetSection(ConfigurationSections.Auth));

        var rsa = RSA.Create();
        if (!string.IsNullOrWhiteSpace(auth.SigningKeyPem))
        {
            rsa.ImportFromPem(auth.SigningKeyPem);
        }
        else
        {
            rsa = RSA.Create(AuthDefaults.DevKeySize);
        }

        var key = new RsaSecurityKey(rsa) { KeyId = auth.KeyId };
        services.AddSingleton(key);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = auth.Issuer,
                    ValidateAudience = true,
                    ValidAudience = auth.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(auth.ClockSkewSeconds),
                    NameClaimType = ClaimNames.Subject,
                    RoleClaimType = ClaimNames.Roles,
                };
                o.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        var token = ctx.Request.Query[AuthDefaults.AccessTokenQueryParameter];
                        if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments(auth.HubPathPrefix))
                        {
                            ctx.Token = token;
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        return services;
    }
}

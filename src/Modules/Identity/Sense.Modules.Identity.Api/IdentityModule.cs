using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.Users;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Infrastructure.Messaging;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Modules.Identity.Infrastructure.Security;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Notifications;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Infrastructure.Notifications;

namespace Sense.Modules.Identity.Api;

/// <summary>Identity modülü kompozisyon kökü: şirket (kiracı) tanımlama, kullanıcı tanımlama, RBAC, davet, JWT.</summary>
public sealed class IdentityModule : IModule
{
    public string Name => IdentityDbContext.SchemaName;

    public IReadOnlyList<Assembly> Assemblies { get; } =
    [
        typeof(IdentityModule).Assembly,
        typeof(IdentityPermissions).Assembly,
        typeof(IdentityDbContext).Assembly,
    ];

    public IEnumerable<Permission> Permissions => IdentityPermissions.All;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<IdentityOptions>().Bind(configuration.GetSection(ConfigurationSections.Identity)).ValidateOnStart();
        services.AddOptions<JwtIssuerOptions>().Bind(configuration.GetSection(ConfigurationSections.Auth));
        services.AddOptions<SecurityOptions>().Bind(configuration.GetSection(SecurityOptions.Section));
        services.AddOptions<FrontendOptions>().Bind(configuration.GetSection(ConfigurationSections.Frontend));
        services.AddOptions<EmailOptions>().Bind(configuration.GetSection(ConfigurationSections.Email));

        services.AddModuleDbContext<IdentityDbContext>(configuration, IdentityDbContext.SchemaName);
        services.AddModuleHandlers(IdentityDbContext.SchemaName, typeof(IdentityPermissions).Assembly, typeof(IdentityDbContext).Assembly);

        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IInvitationRepository, InvitationRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IIdentityReadStore, IdentityReadStore>();

        services.AddScoped<PermissionService>();
        services.AddScoped<IPermissionService>(sp => sp.GetRequiredService<PermissionService>());
        services.AddScoped<IPermissionCacheInvalidator>(sp => sp.GetRequiredService<PermissionService>());
        services.AddScoped<IDataScopeService, DataScopeService>();
        services.AddSingleton<IPermissionCatalog>(sp => new PermissionCatalog(sp.GetRequiredService<IReadOnlyList<IModule>>()));

        services.AddSingleton<IPasswordHasher, AspNetPasswordHasher>();
        services.AddSingleton<ISecretGenerator, SecretGenerator>();
        services.AddSingleton<IEncryptionProvider, AesGcmEncryptionProvider>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<IIdentityMailer, IdentityMailer>();
        services.AddSingleton<IFrontendLinks, FrontendLinks>();

        var emailProvider = configuration.GetSection(ConfigurationSections.Email).Get<EmailOptions>()?.Provider ?? EmailProviders.Smtp;
        if (string.Equals(emailProvider, EmailProviders.None, StringComparison.OrdinalIgnoreCase))
        {
            services.TryAddSingleton<IEmailSender, NullEmailSender>();
        }
        else
        {
            services.TryAddSingleton<IEmailSender, SmtpEmailSender>();
        }
    }
}

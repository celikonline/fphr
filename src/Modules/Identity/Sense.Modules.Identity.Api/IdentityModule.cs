using System.Reflection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.AuditLog;
using Sense.Modules.Identity.Application.Invitations;
using Sense.Modules.Identity.Application.Users;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Infrastructure.Messaging;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Modules.Identity.Infrastructure.Security;
using Sense.Shared.Contracts.Audit;
using Sense.Shared.Contracts.BackgroundJobs;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Notifications;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.BackgroundJobs;
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
        services.AddOptions<FrontendOptions>().Bind(configuration.GetSection(ConfigurationSections.Frontend));
        services.AddOptions<EmailOptions>().Bind(configuration.GetSection(ConfigurationSections.Email));

        // TB-2/S-1 tur 1: Identity'ye özel audit-log yazıcı - yalnız bu DbContext'e bağlanır (bkz. IdentityAuditLogInterceptor).
        services.AddScoped<IdentityAuditLogInterceptor>();
        services.AddModuleDbContext<IdentityDbContext>(
            configuration, IdentityDbContext.SchemaName,
            (sp, options) => options.AddInterceptors(sp.GetRequiredService<IdentityAuditLogInterceptor>()));
        services.AddModuleHandlers(IdentityDbContext.SchemaName, typeof(IdentityPermissions).Assembly, typeof(IdentityDbContext).Assembly);

        // EmployeeTerminatedHandler (G-3-01) gibi doğrudan enjekte edilen integration event handler'ları için
        // (Performance/Assets modüllerindeki I*UnitOfWork kalıbıyla aynı; komut handler'ları UnitOfWorkBehaviour'dan geçer).
        services.AddScoped<IIdentityUnitOfWork>(sp => sp.GetRequiredService<IdentityDbContext>());

        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IInvitationRepository, InvitationRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();
        services.AddScoped<IImpersonationGrantRepository, ImpersonationGrantRepository>();
        services.AddScoped<IUnitAuthorizationRepository, UnitAuthorizationRepository>();
        services.AddScoped<IIdentityReadStore, IdentityReadStore>();

        // senseik-049: audit-log okuma tarafı artık çoklu kaynak - bkz. IAuditLogSource'un doc comment'i.
        services.AddScoped<IAuditLogSource, IdentityAuditLogSource>();

        services.AddScoped<PermissionService>();
        services.AddScoped<IPermissionService>(sp => sp.GetRequiredService<PermissionService>());
        services.AddScoped<IPermissionCacheInvalidator>(sp => sp.GetRequiredService<PermissionService>());
        services.AddScoped<IDataScopeService, DataScopeService>();
        services.AddScoped<IAccessSettingsService, AccessSettingsService>();
        services.AddScoped<ModuleAccessService>();
        services.AddScoped<IModuleAccessService>(sp => sp.GetRequiredService<ModuleAccessService>());
        services.AddScoped<IModuleSettingsCacheInvalidator>(sp => sp.GetRequiredService<ModuleAccessService>());
        services.AddScoped<IImpersonationSessionValidator, ImpersonationSessionValidator>();

        services.AddScoped<AccessTypeGuard>();
        services.AddScoped<Sense.Modules.Identity.Application.Roles.RolePermissionGrantGuard>();
        services.AddScoped<EmployeeInvitationService>();

        // Mevcut kiracıların sistem rollerine yeni izin anahtarlarını taşır (API başlangıcı).
        services.AddScoped<SystemRolePermissionSynchronizer>();
        services.AddHostedService<SystemRolePermissionSyncHostedService>();

        // Cross-module read contract consumed by Shared (G-1-13): BackgroundJobSeedSynchronizer, her yazılabilir
        // kiracı için KVKK md.7 seed'lerini (DocumentExpiryReminder, CandidateRetentionSweep vb.) API başlangıcında kaydeder.
        services.AddScoped<IActiveTenantDirectory, TenantDirectory>();
        services.AddScoped<BackgroundJobSeedSynchronizer>();
        services.AddHostedService<BackgroundJobSeedSyncHostedService>();

        // Cross-module read contract consumed by Employee (hesap durumu / erişim tipi / son davet)
        services.AddScoped<IUserAccountLookup, UserAccountLookup>();

        // Cross-module read contract consumed by Workflow (yönetici/politika bulunamayan taleplerin rol kuyruğu)
        services.AddScoped<IRoleAssignmentLookup, RoleAssignmentLookup>();

        // Cross-module read contract consumed by Notifications (alıcı e-postası/dili)
        services.AddScoped<IUserProfileLookup, UserProfileLookup>();

        // Cross-module read contract consumed by Career (Faz 9): slug → tenantId, pasif kiracı için null (ADR-0007)
        services.AddScoped<ITenantSlugLookup, TenantSlugLookup>();

        // Cross-module read contract consumed by Attendance (S-31 self-servis check-in): tenant IANA saat dilimi
        services.AddScoped<ITenantTimeZoneLookup, TenantTimeZoneLookup>();
        services.AddSingleton<IPermissionCatalog>(sp => new PermissionCatalog(sp.GetRequiredService<IReadOnlyList<IModule>>()));

        services.AddSingleton<IPasswordHasher, AspNetPasswordHasher>();
        services.AddSingleton<ISecretGenerator, SecretGenerator>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<IIdentityMailer, IdentityMailer>();
        services.AddSingleton<IFrontendLinks, FrontendLinks>();

        // MFA (Faz 3 Tur 3): TOTP BCL ile (yeni paket yok); mfaToken durumsuz — DataProtection zaten shared framework'te,
        // ek NuGet gerekmiyor. AddDataProtection() birden fazla modülden çağrılsa da güvenli (idempotent).
        services.AddDataProtection();
        services.AddSingleton<ITotpService, TotpService>();
        services.AddSingleton<IMfaChallengeTokenProtector, MfaChallengeTokenProtector>();

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

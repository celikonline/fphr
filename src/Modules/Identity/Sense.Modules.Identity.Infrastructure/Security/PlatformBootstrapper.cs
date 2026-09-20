using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Modules.Identity.Domain.Users;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Context;

namespace Sense.Modules.Identity.Infrastructure.Security;

/// <summary>
/// Süper admin (platform) hesabını yapılandırmadan (<see cref="PlatformOptions"/>) idempotent oluşturur: "platform" kiracısı,
/// sistem rolleri (SuperAdmin dahil) ve <c>IsPlatformStaff</c> kullanıcı. Yapılandırma yoksa hiçbir şey yapmaz.
/// Mevcut hesabın parolasına dokunmaz (yapılandırma parolasını değiştirmek hesabı sıfırlamaz).
/// </summary>
public sealed partial class PlatformBootstrapper(
    ITenantRepository tenants,
    IUserRepository users,
    IRoleRepository roles,
    SystemRolePermissionSynchronizer roleSynchronizer,
    ITenantContextSetter tenantSetter,
    IPasswordHasher hasher,
    IIdentityUnitOfWork unitOfWork,
    IOptions<PlatformOptions> platformOptions,
    IOptions<IdentityOptions> identityOptions,
    TimeProvider clock,
    ILogger<PlatformBootstrapper> logger)
{
    /// <returns>true: hesap hazır (oluşturuldu veya zaten vardı); false: yapılandırma eksik/geçersiz olduğu için atlandı.</returns>
    public async Task<bool> EnsureAsync(CancellationToken ct)
    {
        var cfg = platformOptions.Value;
        if (!cfg.IsConfigured)
        {
            return false;
        }

        var identity = identityOptions.Value;
        var password = cfg.Password!;
        if (password.Length < identity.MinPasswordLength || !PasswordPolicy.IsStrongEnough(password))
        {
            LogWeakPassword(logger);
            return false;
        }

        var tenant = await tenants.GetBySlugAsync(PlatformTenant.Slug, ct).ConfigureAwait(false);
        if (tenant is null)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var created = Tenant.Create(PlatformTenant.Slug, PlatformTenant.Name, now, trialDays: 0, identity.DefaultLocale, identity.DefaultTimeZone);
            if (created.IsFailure)
            {
                LogTenantCreateFailed(logger, created.Error.Code);
                return false;
            }

            tenant = created.Value;

            // Platform kiracısının kurulum sihirbazı yoktur: süper admin doğrudan konsola girer.
            tenant.CompleteSetup(now);
            tenants.Add(tenant);
            await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        // Eksik sistem rollerini (platformda SuperAdmin dahil) oluşturur; kendi kiracı kapsamını açar ve kaydeder.
        await roleSynchronizer.SyncTenantAsync(tenant.Id, ct).ConfigureAwait(false);

        using var scope = tenantSetter.BeginScope(tenant.Id, tenant.Slug);

        var superAdminRole = await roles.GetByCodeAsync(SystemRoleCodes.SuperAdmin, ct).ConfigureAwait(false);
        if (superAdminRole is null)
        {
            LogRoleMissing(logger);
            return false;
        }

        var email = cfg.Email!.Trim();
        var user = await users.GetByEmailAsync(email, ct).ConfigureAwait(false);
        if (user is null)
        {
            var userResult = User.Create(tenant.Id, email, cfg.FirstName, cfg.LastName, identity.DefaultLanguage);
            if (userResult.IsFailure)
            {
                LogUserCreateFailed(logger, userResult.Error.Code);
                return false;
            }

            user = userResult.Value;
            user.SetPassword(hasher.Hash(password));
            user.SetPlatformStaff(true);
            user.SetRoles([superAdminRole.Id]);
            users.Add(user);
        }
        else
        {
            user.SetPlatformStaff(true);
            user.SetRoles([.. user.Roles.Select(r => r.RoleId), superAdminRole.Id]);
        }

        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    [LoggerMessage(EventId = 2110, Level = LogLevel.Error, Message = "Platform super admin bootstrap skipped: configured password is too short or too weak")]
    private static partial void LogWeakPassword(ILogger logger);

    [LoggerMessage(EventId = 2111, Level = LogLevel.Error, Message = "Platform tenant could not be created: {ErrorCode}")]
    private static partial void LogTenantCreateFailed(ILogger logger, string errorCode);

    [LoggerMessage(EventId = 2112, Level = LogLevel.Error, Message = "Platform super admin bootstrap skipped: SuperAdmin role is missing in the platform tenant")]
    private static partial void LogRoleMissing(ILogger logger);

    [LoggerMessage(EventId = 2113, Level = LogLevel.Error, Message = "Platform super admin user could not be created: {ErrorCode}")]
    private static partial void LogUserCreateFailed(ILogger logger, string errorCode);
}

/// <summary>API başlangıcında süper admin hesabını bir kez sağlar; hata başlatmayı durdurmaz (bir sonraki başlatmada tekrar denenir).</summary>
public sealed partial class PlatformBootstrapHostedService(IServiceScopeFactory scopes, ILogger<PlatformBootstrapHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var system = CurrentUserAccessor.UseSystem();
            await using var scope = scopes.CreateAsyncScope();
            var bootstrapper = scope.ServiceProvider.GetRequiredService<PlatformBootstrapper>();
            await bootstrapper.EnsureAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Veritabanı henüz migrate edilmemiş olabilir (Migrator ayrı çalışır) ya da başka bir örnek aynı anda oluşturmuş olabilir.
            LogBootstrapFailed(logger, ex);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(EventId = 2114, Level = LogLevel.Warning, Message = "Platform super admin bootstrap failed; it will be retried on next start")]
    private static partial void LogBootstrapFailed(ILogger logger, Exception exception);
}

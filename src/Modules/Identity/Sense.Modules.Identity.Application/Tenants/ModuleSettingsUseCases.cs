using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Identity.Application.Tenants;

/// <summary>Modüller sayfasının tek satırı: katalogdaki modül, temel (kapatılamaz) mı, bu kiracı için açık mı.</summary>
public sealed record ModuleSettingItemDto(string Key, bool IsCore, bool Enabled);

/// <summary>
/// Ayarlar &gt; Sistem ayarları &gt; Modüller (senseik-057). <see cref="Modules"/> tüm katalog (<see cref="ProductModules.All"/>
/// sırasıyla); <see cref="DisabledModules"/> kiracının kaydettiği kapalı anahtarlar (PUT gövdesiyle aynı biçim).
/// </summary>
public sealed record ModuleSettingsDto(IReadOnlyList<ModuleSettingItemDto> Modules, IReadOnlyList<string> DisabledModules)
{
    public static ModuleSettingsDto From(TenantModuleSettings settings) => new(
        ProductModules.All.Select(m => new ModuleSettingItemDto(m.Key, !m.IsToggleable, !settings.IsDisabled(m.Key))).ToList(),
        settings.DisabledModules);
}

/// <summary>Modül ayarlarını okur. Erişim ayarları/güvenlik politikasıyla aynı desen: "identity.tenant.view" yeterli,
/// düzenleme ayrı ve daha dar bir izin ister (<see cref="UpdateModuleSettingsCommand"/>). Tüm kullanıcılar için
/// hafif biçim /identity/me <c>features.disabledModules</c> içinde döner.</summary>
[RequiresPermission(IdentityPermissions.TenantView)]
public sealed record GetModuleSettingsQuery : IQuery<ModuleSettingsDto>;

public sealed class GetModuleSettingsHandler(ITenantRepository tenants, ITenantContext tenantContext) : IQueryHandler<GetModuleSettingsQuery, ModuleSettingsDto>
{
    public async Task<Result<ModuleSettingsDto>> Handle(GetModuleSettingsQuery query, CancellationToken cancellationToken)
    {
        var tenant = await tenants.GetByIdAsync(tenantContext.TenantId, cancellationToken).ConfigureAwait(false);
        if (tenant is null)
        {
            return Error.NotFound(IdentityErrors.TenantNotFound);
        }

        return ModuleSettingsDto.From(tenant.ModuleSettings);
    }
}

/// <summary>
/// Modüller sayfasının "Kaydet" eylemi: kapalı modül listesinin tamamı birlikte değiştirilir (listede olmayan = açık).
/// Bilinmeyen ya da temel (kapatılamaz) bir anahtar tüm isteği reddeder. Kapatmak veri silmez, yalnız gizler/engeller.
/// </summary>
[RequiresPermission(IdentityPermissions.ModulesManage)]
public sealed record UpdateModuleSettingsCommand(IReadOnlyList<string> DisabledModules) : ICommand;

public sealed class UpdateModuleSettingsHandler(
    ITenantRepository tenants,
    ITenantContext tenantContext,
    IModuleSettingsCacheInvalidator cache) : ICommandHandler<UpdateModuleSettingsCommand>
{
    public async Task<Result> Handle(UpdateModuleSettingsCommand command, CancellationToken cancellationToken)
    {
        var requested = command.DisabledModules ?? [];
        var validation = Validate(requested);
        if (validation.IsFailure)
        {
            return validation;
        }

        var tenant = await tenants.GetByIdAsync(tenantContext.TenantId, cancellationToken).ConfigureAwait(false);
        if (tenant is null)
        {
            return Error.NotFound(IdentityErrors.TenantNotFound);
        }

        tenant.UpdateModuleSettings(TenantModuleSettings.WithDisabled(requested));
        await cache.InvalidateTenantAsync(tenant.Id, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    /// <summary>Her anahtar katalogda olmalı ve kapatılabilir olmalı; ilk hatalı anahtar {module} parametresiyle döner.</summary>
    public static Result Validate(IEnumerable<string> disabledModules)
    {
        foreach (var key in disabledModules)
        {
            var module = ProductModules.Find(key?.Trim());
            if (module is null)
            {
                return Error.Validation(ProductModuleErrors.Unknown, (ProductModuleErrors.ModuleArg, key));
            }

            if (!module.IsToggleable)
            {
                return Error.Validation(ProductModuleErrors.NotToggleable, (ProductModuleErrors.ModuleArg, key));
            }
        }

        return Result.Success();
    }
}

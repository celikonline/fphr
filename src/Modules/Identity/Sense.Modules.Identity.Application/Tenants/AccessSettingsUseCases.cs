using Sense.Modules.Identity.Contracts;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Identity.Application.Tenants;

/// <summary>
/// Erişim ayarları sayfasının (Ayarlar &gt; Sistem ayarları &gt; Erişim ayarları) 11 anahtarı, ana sayfa/hesap/takvim
/// bölümlerine göre gruplanmış. <see cref="Domain.Tenants.TenantAccessSettings"/> ile bire bir eşleşir.
/// </summary>
public sealed record AccessSettingsDto(
    bool ShowOrgInfo,
    bool ShowUpcomingLeaves,
    bool ShowUpcomingBirthdays,
    bool ShowEventCalendar,
    bool ShowFiles,
    bool ShowAssets,
    bool ShowWorkPhoneNumbers,
    bool ShowOrgChart,
    bool ShowCustomFields,
    bool ShowCalendarMenu,
    bool ShowAllLeavesInCalendar)
{
    public static readonly AccessSettingsDto AllEnabled = new(true, true, true, true, true, true, true, true, true, true, true);

    public static AccessSettingsDto From(TenantAccessSettings s) => new(
        s.ShowOrgInfo, s.ShowUpcomingLeaves, s.ShowUpcomingBirthdays, s.ShowEventCalendar, s.ShowFiles, s.ShowAssets,
        s.ShowWorkPhoneNumbers, s.ShowOrgChart, s.ShowCustomFields,
        s.ShowCalendarMenu, s.ShowAllLeavesInCalendar);

    public TenantAccessSettings ToDomain() => new()
    {
        ShowOrgInfo = ShowOrgInfo,
        ShowUpcomingLeaves = ShowUpcomingLeaves,
        ShowUpcomingBirthdays = ShowUpcomingBirthdays,
        ShowEventCalendar = ShowEventCalendar,
        ShowFiles = ShowFiles,
        ShowAssets = ShowAssets,
        ShowWorkPhoneNumbers = ShowWorkPhoneNumbers,
        ShowOrgChart = ShowOrgChart,
        ShowCustomFields = ShowCustomFields,
        ShowCalendarMenu = ShowCalendarMenu,
        ShowAllLeavesInCalendar = ShowAllLeavesInCalendar,
    };
}

/// <summary>
/// <see cref="AccessSettingKeys"/> anahtarlarını <see cref="TenantAccessSettings"/> alanlarına eşler.
/// Identity.Infrastructure'daki IAccessSettingsService bu kataloğu kullanır (tek anahtar sorgusu için).
/// </summary>
public static class AccessSettingsRegistry
{
    public static readonly IReadOnlyDictionary<string, Func<TenantAccessSettings, bool>> Getters =
        new Dictionary<string, Func<TenantAccessSettings, bool>>(StringComparer.Ordinal)
        {
            [AccessSettingKeys.HomeShowOrgInfo] = s => s.ShowOrgInfo,
            [AccessSettingKeys.HomeShowUpcomingLeaves] = s => s.ShowUpcomingLeaves,
            [AccessSettingKeys.HomeShowUpcomingBirthdays] = s => s.ShowUpcomingBirthdays,
            [AccessSettingKeys.HomeShowEventCalendar] = s => s.ShowEventCalendar,
            [AccessSettingKeys.HomeShowFiles] = s => s.ShowFiles,
            [AccessSettingKeys.HomeShowAssets] = s => s.ShowAssets,
            [AccessSettingKeys.AccountShowWorkPhoneNumbers] = s => s.ShowWorkPhoneNumbers,
            [AccessSettingKeys.AccountShowOrgChart] = s => s.ShowOrgChart,
            [AccessSettingKeys.AccountShowCustomFields] = s => s.ShowCustomFields,
            [AccessSettingKeys.CalendarShowMenu] = s => s.ShowCalendarMenu,
            [AccessSettingKeys.CalendarShowAllLeaves] = s => s.ShowAllLeavesInCalendar,
        };
}

/// <summary>Erişim ayarlarını okur (mevcut kiracı bağlamı). "identity.tenant.view" ile aynı yetki: profilin bir parçası.</summary>
[RequiresPermission(IdentityPermissions.TenantView)]
public sealed record GetAccessSettingsQuery : IQuery<AccessSettingsDto>;

public sealed class GetAccessSettingsHandler(ITenantRepository tenants, ITenantContext tenantContext) : IQueryHandler<GetAccessSettingsQuery, AccessSettingsDto>
{
    public async Task<Result<AccessSettingsDto>> Handle(GetAccessSettingsQuery query, CancellationToken cancellationToken)
    {
        var tenant = await tenants.GetByIdAsync(tenantContext.TenantId, cancellationToken).ConfigureAwait(false);
        if (tenant is null)
        {
            return Error.NotFound(IdentityErrors.TenantNotFound);
        }

        return AccessSettingsDto.From(tenant.AccessSettings);
    }
}

/// <summary>Erişim ayarları sayfasının "Kaydet" eylemi: 11 anahtar birlikte, anında etkili biçimde güncellenir.</summary>
[RequiresPermission(IdentityPermissions.TenantManage)]
public sealed record UpdateAccessSettingsCommand(
    bool ShowOrgInfo,
    bool ShowUpcomingLeaves,
    bool ShowUpcomingBirthdays,
    bool ShowEventCalendar,
    bool ShowFiles,
    bool ShowAssets,
    bool ShowWorkPhoneNumbers,
    bool ShowOrgChart,
    bool ShowCustomFields,
    bool ShowCalendarMenu,
    bool ShowAllLeavesInCalendar) : ICommand
{
    public AccessSettingsDto ToDto() => new(
        ShowOrgInfo, ShowUpcomingLeaves, ShowUpcomingBirthdays, ShowEventCalendar, ShowFiles, ShowAssets,
        ShowWorkPhoneNumbers, ShowOrgChart, ShowCustomFields, ShowCalendarMenu, ShowAllLeavesInCalendar);
}

public sealed class UpdateAccessSettingsHandler(ITenantRepository tenants, ITenantContext tenantContext) : ICommandHandler<UpdateAccessSettingsCommand>
{
    public async Task<Result> Handle(UpdateAccessSettingsCommand command, CancellationToken cancellationToken)
    {
        var tenant = await tenants.GetByIdAsync(tenantContext.TenantId, cancellationToken).ConfigureAwait(false);
        if (tenant is null)
        {
            return Error.NotFound(IdentityErrors.TenantNotFound);
        }

        tenant.UpdateAccessSettings(command.ToDto().ToDomain());
        return Result.Success();
    }
}

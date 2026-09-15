using Sense.Modules.Identity.Domain.Tenants;
using Shouldly;
using Xunit;

namespace Sense.Modules.Identity.Tests.Tenants;

/// <summary>Domain düzeyinde: varsayılan değerler ve Tenant.UpdateAccessSettings ile birlikte güncelleme.</summary>
public sealed class TenantAccessSettingsTests
{
    [Fact]
    public void Default_HasAllVisibilityOn_ExceptCustomFieldsAndAllLeaves()
    {
        var defaults = TenantAccessSettings.Default;

        // 11.3.13 açık nokta #6: yalnız bu ikisi için varsayılan "kapalı" kaynakta doğrulandı; geri kalanı opt-out (açık).
        defaults.ShowCustomFields.ShouldBeFalse();
        defaults.ShowAllLeavesInCalendar.ShouldBeFalse();

        defaults.ShowOrgInfo.ShouldBeTrue();
        defaults.ShowUpcomingLeaves.ShouldBeTrue();
        defaults.ShowUpcomingBirthdays.ShouldBeTrue();
        defaults.ShowEventCalendar.ShouldBeTrue();
        defaults.ShowFiles.ShouldBeTrue();
        defaults.ShowAssets.ShouldBeTrue();
        defaults.ShowWorkPhoneNumbers.ShouldBeTrue();
        defaults.ShowOrgChart.ShouldBeTrue();
        defaults.ShowCalendarMenu.ShouldBeTrue();
    }

    [Fact]
    public void NewTenant_StartsWithDefaultAccessSettings()
    {
        var tenant = Tenant.Create("acme", "Acme", DateTime.UtcNow, trialDays: 0, "tr-TR", "Europe/Istanbul").Value;

        tenant.AccessSettings.ShouldBe(TenantAccessSettings.Default);
    }

    [Fact]
    public void UpdateAccessSettings_ReplacesAllElevenKeysAtOnce()
    {
        var tenant = Tenant.Create("acme", "Acme", DateTime.UtcNow, trialDays: 0, "tr-TR", "Europe/Istanbul").Value;

        var updated = new TenantAccessSettings
        {
            ShowOrgInfo = false,
            ShowUpcomingLeaves = false,
            ShowUpcomingBirthdays = false,
            ShowEventCalendar = false,
            ShowFiles = false,
            ShowAssets = false,
            ShowWorkPhoneNumbers = false,
            ShowOrgChart = false,
            ShowCustomFields = true,
            ShowCalendarMenu = false,
            ShowAllLeavesInCalendar = true,
        };

        tenant.UpdateAccessSettings(updated);

        tenant.AccessSettings.ShouldBe(updated);
    }
}

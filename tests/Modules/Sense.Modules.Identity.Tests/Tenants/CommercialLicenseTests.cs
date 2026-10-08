using Sense.Modules.Identity.Application.Tenants;
using Sense.Modules.Identity.Domain.Tenants;
using Xunit;

namespace Sense.Modules.Identity.Tests.Tenants;

public sealed class CommercialLicenseTests
{
    private static readonly DateTime Now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
    private static Tenant Create() => Tenant.Create("sales-test", "Sales Test", Now, 14, "tr-TR", "Europe/Istanbul").Value;
    private static CommercialLicense License(string status = "trial", DateTime? expires = null) =>
        new(status, "growth", ["employee", "leave"], 25, expires ?? Now.AddDays(14), "lead-123");

    [Fact]
    public void ExistingSettingsAndLegacyAccessArePreserved()
    {
        var tenant = Create();
        tenant.UpdateSettings("{\"customSetting\":\"keep\"}");
        Assert.True(CommercialLicenseRules.CanAccess(tenant, Now));
        tenant.UpdateSettings(License().WriteTo(tenant.SettingsJson));
        Assert.Contains("customSetting", tenant.SettingsJson);
        Assert.Equal(25, CommercialLicense.Read(tenant.SettingsJson)!.EmployeeLimit);
    }

    [Theory]
    [InlineData("revoked")]
    [InlineData("unknown")]
    public void RevokedAndUnknownStatesDenyAccess(string status)
    {
        var tenant = Create(); tenant.UpdateSettings(License(status).WriteTo(tenant.SettingsJson));
        Assert.False(CommercialLicenseRules.CanAccess(tenant, Now));
    }

    [Fact]
    public void ExactExpiryBoundaryDeniesAccess()
    {
        var tenant = Create(); tenant.UpdateSettings(License(expires: Now).WriteTo(tenant.SettingsJson));
        Assert.False(CommercialLicenseRules.CanAccess(tenant, Now));
    }

    [Fact]
    public void TenantModuleToggleCannotEnableAnUnlicensedModule()
    {
        var tenant = Create(); tenant.UpdateSettings(License().WriteTo(tenant.SettingsJson));
        tenant.UpdateModuleSettings(TenantModuleSettings.WithDisabled([]));
        Assert.Contains("expense", CommercialLicenseRules.DisabledModules(tenant));
        Assert.DoesNotContain("leave", CommercialLicenseRules.DisabledModules(tenant));
        tenant.UpdateModuleSettings(TenantModuleSettings.WithDisabled(["leave"]));
        Assert.Contains("leave", CommercialLicenseRules.DisabledModules(tenant));
    }

    [Fact]
    public void LicenseUpgradeUnlocksPurchasedModules()
    {
        var tenant = Create(); tenant.UpdateSettings(License().WriteTo(tenant.SettingsJson));
        tenant.UpdateSettings((License("active") with { Modules = ["employee", "leave", "expense"] }).WriteTo(tenant.SettingsJson));
        Assert.DoesNotContain("expense", CommercialLicenseRules.DisabledModules(tenant));
        Assert.True(CommercialLicenseRules.CanAccess(tenant, Now));
    }

    [Fact]
    public void InvalidModuleAndCapacityAreRejected()
    {
        var validator = new CommercialLicenseValidator();
        Assert.False(validator.Validate(License() with { Modules = ["invented"], EmployeeLimit = 0 }).IsValid);
        Assert.True(validator.Validate(License()).IsValid);
    }
}

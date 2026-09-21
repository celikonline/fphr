using NSubstitute;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.Tenants;
using Sense.Modules.Identity.Application.Roles;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Identity.Tests.Tenants;

/// <summary>
/// senseik-057 "Ayarlar → Modüller": TenantModuleSettings (domain), ProductModules kataloğu ve
/// Get/UpdateModuleSettingsHandler (doğrulama, kalıcılık, önbellek geçersizleme, kiracı izolasyonu).
/// </summary>
public sealed class ModuleSettingsUseCasesTests
{
    private static Tenant NewTenant(string slug) => Tenant.Create(slug, slug, DateTime.UtcNow, trialDays: 0, "tr-TR", "Europe/Istanbul").Value;

    private static (ITenantRepository Tenants, ITenantContext Context) RepoFor(params Tenant[] all)
    {
        var tenants = Substitute.For<ITenantRepository>();
        foreach (var tenant in all)
        {
            tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        }

        var context = Substitute.For<ITenantContext>();
        context.IsResolved.Returns(true);
        context.TenantId.Returns(all[0].Id);
        return (tenants, context);
    }

    // --- Domain ---

    [Fact]
    public void Default_HasNoDisabledModules()
    {
        TenantModuleSettings.Default.DisabledModules.ShouldBeEmpty();
        NewTenant("acme").ModuleSettings.DisabledModules.ShouldBeEmpty();
    }

    [Fact]
    public void WithDisabled_TrimsDedupesAndSorts_DroppingBlanks()
    {
        var settings = TenantModuleSettings.WithDisabled([" payroll", "leave", "payroll", "", "  "]);

        settings.DisabledModules.ShouldBe(["leave", "payroll"]);
        settings.IsDisabled("leave").ShouldBeTrue();
        settings.IsDisabled("assets").ShouldBeFalse();
    }

    // --- Catalog ---

    [Theory]
    [InlineData("Sense.Modules.Leave.Api", "leave", true)]
    [InlineData("Sense.Modules.OccupationalSafety.Api", "occupationalSafety", true)]
    [InlineData("Sense.Modules.Training.Api", "training", true)]
    [InlineData("Sense.Modules.Identity.Api", "identity", false)]
    [InlineData("Sense.Modules.DataTransfer.Api", "dataTransfer", false)]
    public void FromAssemblyName_MapsModuleApiAssemblies(string assemblyName, string expectedKey, bool toggleable)
    {
        var module = ProductModules.FromAssemblyName(assemblyName);

        module.ShouldNotBeNull();
        module.Key.ShouldBe(expectedKey);
        module.IsToggleable.ShouldBe(toggleable);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Sense.Api")]
    [InlineData("Sense.Shared.Web")]
    [InlineData("Sense.Modules.Unknown.Api")]
    public void FromAssemblyName_ReturnsNull_ForNonModuleOrUncataloguedAssemblies(string? assemblyName) =>
        ProductModules.FromAssemblyName(assemblyName).ShouldBeNull();

    [Fact]
    public void Catalog_CoreAndToggleableSets_AreAsSpecified()
    {
        ProductModules.All.Where(m => !m.IsToggleable).Select(m => m.Key).ShouldBe(
            ["identity", "organization", "employee", "workflow", "notifications", "documents", "dataTransfer", "reporting", "analytics", "integrations"],
            ignoreOrder: true);
        ProductModules.All.Where(m => m.IsToggleable).Select(m => m.Key).ShouldBe(
            ["leave", "attendance", "payroll", "recruitment", "expense", "assets", "performance", "announcements", "occupationalSafety", "training"],
            ignoreOrder: true);
        ProductModules.All.Select(m => m.Key).ShouldBeUnique();
    }

    // --- Handlers ---

    [Fact]
    public async Task Get_ReturnsWholeCatalog_AllEnabled_ForFreshTenant()
    {
        var tenant = NewTenant("acme");
        var (tenants, context) = RepoFor(tenant);

        var result = await new GetModuleSettingsHandler(tenants, context).Handle(new GetModuleSettingsQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.DisabledModules.ShouldBeEmpty();
        result.Value.Modules.Count.ShouldBe(ProductModules.All.Count);
        result.Value.Modules.ShouldAllBe(m => m.Enabled);
        result.Value.Modules.Single(m => m.Key == "employee").IsCore.ShouldBeTrue();
        result.Value.Modules.Single(m => m.Key == "leave").IsCore.ShouldBeFalse();
    }

    [Fact]
    public async Task Get_ReturnsNotFound_WhenTenantMissing()
    {
        var tenants = Substitute.For<ITenantRepository>();
        var context = Substitute.For<ITenantContext>();
        context.TenantId.Returns(Guid.NewGuid());

        var result = await new GetModuleSettingsHandler(tenants, context).Handle(new GetModuleSettingsQuery(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(IdentityErrors.TenantNotFound);
    }

    [Fact]
    public async Task Update_PersistsNormalizedList_InvalidatesCache_AndGetReflectsIt()
    {
        var tenant = NewTenant("acme");
        var (tenants, context) = RepoFor(tenant);
        var cache = Substitute.For<IModuleSettingsCacheInvalidator>();

        var result = await new UpdateModuleSettingsHandler(tenants, context, cache)
            .Handle(new UpdateModuleSettingsCommand(["payroll", "leave", "leave"]), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        tenant.ModuleSettings.DisabledModules.ShouldBe(["leave", "payroll"]);
        await cache.Received(1).InvalidateTenantAsync(tenant.Id, Arg.Any<CancellationToken>());

        var read = await new GetModuleSettingsHandler(tenants, context).Handle(new GetModuleSettingsQuery(), CancellationToken.None);
        read.Value.Modules.Single(m => m.Key == "leave").Enabled.ShouldBeFalse();
        read.Value.Modules.Single(m => m.Key == "assets").Enabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Update_EmptyList_ReenablesEverything()
    {
        var tenant = NewTenant("acme");
        tenant.UpdateModuleSettings(TenantModuleSettings.WithDisabled(["leave"]));
        var (tenants, context) = RepoFor(tenant);

        var result = await new UpdateModuleSettingsHandler(tenants, context, Substitute.For<IModuleSettingsCacheInvalidator>())
            .Handle(new UpdateModuleSettingsCommand([]), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        tenant.ModuleSettings.DisabledModules.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("employee", "module.not_toggleable")]
    [InlineData("identity", "module.not_toggleable")]
    [InlineData("billing", "module.unknown")]
    [InlineData("Leave", "module.unknown")]
    public async Task Update_RejectsCoreOrUnknownKeys_WithValidationError_AndChangesNothing(string key, string expectedCode)
    {
        var tenant = NewTenant("acme");
        var (tenants, context) = RepoFor(tenant);
        var cache = Substitute.For<IModuleSettingsCacheInvalidator>();

        var result = await new UpdateModuleSettingsHandler(tenants, context, cache)
            .Handle(new UpdateModuleSettingsCommand(["leave", key]), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe(expectedCode);
        result.Error.Args!["module"].ShouldBe(key);
        tenant.ModuleSettings.DisabledModules.ShouldBeEmpty();
        await cache.DidNotReceiveWithAnyArgs().InvalidateTenantAsync(default, default);
    }

    [Fact]
    public async Task Update_TenantIsolation_OnlyTouchesCurrentTenant()
    {
        var tenantA = NewTenant("tenant-a");
        var tenantB = NewTenant("tenant-b");
        var (tenants, context) = RepoFor(tenantA, tenantB);

        (await new UpdateModuleSettingsHandler(tenants, context, Substitute.For<IModuleSettingsCacheInvalidator>())
            .Handle(new UpdateModuleSettingsCommand(["expense"]), CancellationToken.None)).IsSuccess.ShouldBeTrue();

        tenantA.ModuleSettings.DisabledModules.ShouldBe(["expense"]);
        tenantB.ModuleSettings.DisabledModules.ShouldBeEmpty();
    }

    [Fact]
    public void ModulesManagePermission_IsInCatalog_AndFollowsNamingConvention()
    {
        IdentityPermissions.All.ShouldContain(p => p.Key == IdentityPermissions.ModulesManage);
        IdentityPermissions.ModulesManage.ShouldBe("identity.modules.manage");
    }

    [Theory]
    [InlineData("Owner", true)]
    [InlineData("Admin", true)]
    [InlineData("HR", false)]
    [InlineData("Employee", false)]
    public void ModulesManagePermission_GoesToOwnerAndAdminOnly(string roleCode, bool expected)
    {
        var definition = SystemRoleDefinitions.All.Single(d => d.Code == roleCode);

        var granted = definition.Resolve(Guid.NewGuid(), IdentityPermissions.All).Select(p => p.Permission);

        granted.Contains(IdentityPermissions.ModulesManage).ShouldBe(expected);
    }
}

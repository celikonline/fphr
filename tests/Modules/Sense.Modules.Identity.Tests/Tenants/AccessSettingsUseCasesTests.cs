using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.Tenants;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Shared.Contracts.Context;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Sense.Modules.Identity.Tests.Tenants;

/// <summary>
/// GetAccessSettingsHandler/UpdateAccessSettingsHandler: round-trip ve kiracı izolasyonu.
/// Handler'lar yalnızca ITenantContext.TenantId'nin işaret ettiği kiracıyı okur/yazar; başka bir kiracının
/// verisi asla karışmaz (repository sahte uygulaması iki farklı kiracıyı ayrı ayrı tutar).
/// </summary>
public sealed class AccessSettingsUseCasesTests
{
    private static Tenant NewTenant(string slug) => Tenant.Create(slug, slug, DateTime.UtcNow, trialDays: 0, "tr-TR", "Europe/Istanbul").Value;

    [Fact]
    public async Task Get_ReturnsCurrentTenantSettings_AsDefaults_ForFreshTenant()
    {
        var tenant = NewTenant("acme");
        var tenants = Substitute.For<ITenantRepository>();
        tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenant.Id);

        var handler = new GetAccessSettingsHandler(tenants, tenantContext);
        var result = await handler.Handle(new GetAccessSettingsQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(AccessSettingsDto.From(TenantAccessSettings.Default));
    }

    [Fact]
    public async Task Get_ReturnsNotFound_WhenTenantMissing()
    {
        var tenants = Substitute.For<ITenantRepository>();
        tenants.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Tenant?)null);
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(Guid.NewGuid());

        var handler = new GetAccessSettingsHandler(tenants, tenantContext);
        var result = await handler.Handle(new GetAccessSettingsQuery(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(IdentityErrors.TenantNotFound);
    }

    [Fact]
    public async Task Update_PersistsAllElevenKeys_OnTheCurrentTenantOnly()
    {
        var tenant = NewTenant("acme");
        var tenants = Substitute.For<ITenantRepository>();
        tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenant.Id);

        var command = new UpdateAccessSettingsCommand(
            ShowOrgInfo: false, ShowUpcomingLeaves: false, ShowUpcomingBirthdays: false, ShowEventCalendar: false,
            ShowFiles: false, ShowAssets: false, ShowWorkPhoneNumbers: false, ShowOrgChart: false,
            ShowCustomFields: true, ShowCalendarMenu: false, ShowAllLeavesInCalendar: true);

        var handler = new UpdateAccessSettingsHandler(tenants, tenantContext);
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        tenant.AccessSettings.ShouldBe(command.ToDto().ToDomain());
    }

    [Fact]
    public async Task TenantIsolation_UpdatingOneTenant_NeverTouchesAnother()
    {
        var tenantA = NewTenant("tenant-a");
        var tenantB = NewTenant("tenant-b");
        var tenants = Substitute.For<ITenantRepository>();
        tenants.GetByIdAsync(tenantA.Id, Arg.Any<CancellationToken>()).Returns(tenantA);
        tenants.GetByIdAsync(tenantB.Id, Arg.Any<CancellationToken>()).Returns(tenantB);

        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantA.Id);

        var command = new UpdateAccessSettingsCommand(
            ShowOrgInfo: false, ShowUpcomingLeaves: false, ShowUpcomingBirthdays: false, ShowEventCalendar: false,
            ShowFiles: false, ShowAssets: false, ShowWorkPhoneNumbers: false, ShowOrgChart: false,
            ShowCustomFields: true, ShowCalendarMenu: false, ShowAllLeavesInCalendar: true);

        var handler = new UpdateAccessSettingsHandler(tenants, tenantContext);
        (await handler.Handle(command, CancellationToken.None)).IsSuccess.ShouldBeTrue();

        // Yalnız kiracı A değişti; kiracı B hâlâ varsayılanlarda (izolasyon).
        tenantA.AccessSettings.ShouldNotBe(TenantAccessSettings.Default);
        tenantB.AccessSettings.ShouldBe(TenantAccessSettings.Default);

        // Bağlam kiracı B'ye geçince okunan da B'nin (varsayılan) ayarları olur, A'nın değil.
        tenantContext.TenantId.Returns(tenantB.Id);
        var getHandler = new GetAccessSettingsHandler(tenants, tenantContext);
        var resultB = await getHandler.Handle(new GetAccessSettingsQuery(), CancellationToken.None);
        resultB.Value.ShouldBe(AccessSettingsDto.From(TenantAccessSettings.Default));
    }
}

using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.Tenants;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Shared.Contracts.Context;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Sense.Modules.Identity.Tests.Tenants;

/// <summary>
/// GetSecurityPolicyHandler/UpdateSecurityPolicyHandler (senseik-051): round-trip ve kiracı izolasyonu.
/// Erişim ayarlarıyla aynı desen - handler'lar yalnızca ITenantContext.TenantId'nin işaret ettiği kiracıyı okur/yazar.
/// </summary>
public sealed class SecurityPolicyUseCasesTests
{
    private static Tenant NewTenant(string slug) => Tenant.Create(slug, slug, DateTime.UtcNow, trialDays: 0, "tr-TR", "Europe/Istanbul").Value;

    [Fact]
    public async Task Get_ReturnsCurrentTenantPolicy_AsDefaults_ForFreshTenant()
    {
        var tenant = NewTenant("acme");
        var tenants = Substitute.For<ITenantRepository>();
        tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenant.Id);

        var handler = new GetSecurityPolicyHandler(tenants, tenantContext);
        var result = await handler.Handle(new GetSecurityPolicyQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(SecurityPolicyDto.From(TenantSecurityPolicy.Default));
    }

    [Fact]
    public async Task Get_ReturnsNotFound_WhenTenantMissing()
    {
        var tenants = Substitute.For<ITenantRepository>();
        tenants.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Tenant?)null);
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(Guid.NewGuid());

        var handler = new GetSecurityPolicyHandler(tenants, tenantContext);
        var result = await handler.Handle(new GetSecurityPolicyQuery(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(IdentityErrors.TenantNotFound);
    }

    [Fact]
    public async Task Update_PersistsEnforcedRoleList_OnTheCurrentTenantOnly()
    {
        var tenant = NewTenant("acme");
        var tenants = Substitute.For<ITenantRepository>();
        tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenant.Id);

        var command = new UpdateSecurityPolicyCommand([SystemRoleCodes.Owner, SystemRoleCodes.HR]);

        var handler = new UpdateSecurityPolicyHandler(tenants, tenantContext);
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        tenant.SecurityPolicy.ShouldBe(command.ToDto().ToDomain());
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

        var command = new UpdateSecurityPolicyCommand([SystemRoleCodes.Owner]);

        var handler = new UpdateSecurityPolicyHandler(tenants, tenantContext);
        (await handler.Handle(command, CancellationToken.None)).IsSuccess.ShouldBeTrue();

        // Yalnız kiracı A değişti; kiracı B hâlâ varsayılanlarda (izolasyon).
        tenantA.SecurityPolicy.ShouldNotBe(TenantSecurityPolicy.Default);
        tenantB.SecurityPolicy.ShouldBe(TenantSecurityPolicy.Default);

        // Bağlam kiracı B'ye geçince okunan da B'nin (varsayılan) politikası olur, A'nın değil.
        tenantContext.TenantId.Returns(tenantB.Id);
        var getHandler = new GetSecurityPolicyHandler(tenants, tenantContext);
        var resultB = await getHandler.Handle(new GetSecurityPolicyQuery(), CancellationToken.None);
        resultB.Value.ShouldBe(SecurityPolicyDto.From(TenantSecurityPolicy.Default));
    }
}

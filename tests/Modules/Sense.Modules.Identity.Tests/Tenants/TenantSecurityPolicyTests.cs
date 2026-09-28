using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Domain.Tenants;
using Shouldly;
using Xunit;

namespace Sense.Modules.Identity.Tests.Tenants;

/// <summary>senseik-051: Domain düzeyinde zorunlu 2FA politikası - varsayılan değer, Tenant.UpdateSecurityPolicy
/// ile birlikte güncelleme ve RequiresMfaSetup karar mantığı (zorunlu rol + MFA kapalı → true).</summary>
public sealed class TenantSecurityPolicyTests
{
    [Fact]
    public void Default_HasNoEnforcedRoles()
    {
        TenantSecurityPolicy.Default.RequireMfaForRoles.ShouldBeEmpty();
    }

    [Fact]
    public void NewTenant_StartsWithDefaultSecurityPolicy()
    {
        var tenant = Tenant.Create("acme", "Acme", DateTime.UtcNow, trialDays: 0, "tr-TR", "Europe/Istanbul").Value;

        tenant.SecurityPolicy.ShouldBe(TenantSecurityPolicy.Default);
    }

    [Fact]
    public void UpdateSecurityPolicy_ReplacesEnforcedRoleList()
    {
        var tenant = Tenant.Create("acme", "Acme", DateTime.UtcNow, trialDays: 0, "tr-TR", "Europe/Istanbul").Value;
        var updated = new TenantSecurityPolicy { RequireMfaForRoles = [SystemRoleCodes.Owner, SystemRoleCodes.Admin] };

        tenant.UpdateSecurityPolicy(updated);

        tenant.SecurityPolicy.ShouldBe(updated);
    }

    [Fact]
    public void RequiresMfaSetup_UserHasEnforcedRoleAndMfaOff_ReturnsTrue()
    {
        var policy = new TenantSecurityPolicy { RequireMfaForRoles = [SystemRoleCodes.HR, SystemRoleCodes.PayrollSpecialist] };

        policy.RequiresMfaSetup([SystemRoleCodes.PayrollSpecialist], mfaEnabled: false).ShouldBeTrue();
    }

    [Fact]
    public void RequiresMfaSetup_RoleCodeMatchIsCaseInsensitive()
    {
        var policy = new TenantSecurityPolicy { RequireMfaForRoles = ["Owner"] };

        policy.RequiresMfaSetup(["owner"], mfaEnabled: false).ShouldBeTrue();
    }

    [Fact]
    public void RequiresMfaSetup_MfaAlreadyEnabled_ReturnsFalseEvenWithEnforcedRole()
    {
        var policy = new TenantSecurityPolicy { RequireMfaForRoles = [SystemRoleCodes.Owner] };

        policy.RequiresMfaSetup([SystemRoleCodes.Owner], mfaEnabled: true).ShouldBeFalse();
    }

    [Fact]
    public void RequiresMfaSetup_UserRoleNotInEnforcedList_ReturnsFalse()
    {
        var policy = new TenantSecurityPolicy { RequireMfaForRoles = [SystemRoleCodes.Owner, SystemRoleCodes.Admin] };

        policy.RequiresMfaSetup([SystemRoleCodes.Employee], mfaEnabled: false).ShouldBeFalse();
    }

    [Fact]
    public void RequiresMfaSetup_NoEnforcedRoles_ReturnsFalseRegardlessOfUserRoles()
    {
        TenantSecurityPolicy.Default.RequiresMfaSetup([SystemRoleCodes.Owner], mfaEnabled: false).ShouldBeFalse();
    }

    /// <summary>FG-022: RequiresMfa MFA durumundan bağımsızdır; MFA açık kullanıcı için de true (self-servis kapatmayı engeller).</summary>
    [Fact]
    public void RequiresMfa_UserHasEnforcedRole_ReturnsTrueEvenWhenMfaAlreadyEnabled()
    {
        var policy = new TenantSecurityPolicy { RequireMfaForRoles = [SystemRoleCodes.HR] };

        policy.RequiresMfa([SystemRoleCodes.Employee, "hr"]).ShouldBeTrue();
        policy.RequiresMfaSetup([SystemRoleCodes.HR], mfaEnabled: true).ShouldBeFalse();
    }

    [Fact]
    public void RequiresMfa_RoleNotEnforcedOrEmptyPolicy_ReturnsFalse()
    {
        new TenantSecurityPolicy { RequireMfaForRoles = [SystemRoleCodes.Owner] }.RequiresMfa([SystemRoleCodes.Employee]).ShouldBeFalse();
        TenantSecurityPolicy.Default.RequiresMfa([SystemRoleCodes.Owner]).ShouldBeFalse();
    }
}

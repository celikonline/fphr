using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Roles;
using Shouldly;
using Xunit;

namespace Sense.Modules.Identity.Tests.Roles;

/// <summary>
/// Kullanıcı grubu modeli: müşteri kiracısı sistem rolleri Owner + İK Yöneticisi + Muhasebe + Müdür + Çalışan; kaldırılan eski
/// kodlar bir gruba devredilir ve özel rol kodu olarak kullanılamaz.
/// </summary>
public sealed class SystemRoleCodesTests
{
    [Fact]
    public void All_IsOwnerPlusTheUserGroups()
    {
        SystemRoleCodes.All.ShouldBe([SystemRoleCodes.Owner, SystemRoleCodes.HR, SystemRoleCodes.PayrollSpecialist, SystemRoleCodes.Manager, SystemRoleCodes.Employee]);
        SystemRoleCodes.UserGroups.ShouldBe([SystemRoleCodes.HR, SystemRoleCodes.PayrollSpecialist, SystemRoleCodes.Manager, SystemRoleCodes.Employee]);
        SystemRoleCodes.RetiredSuccessors.ShouldNotContainKey(SystemRoleCodes.Manager);
        SystemRoleCodes.UserGroups.ShouldNotContain(SystemRoleCodes.Owner);
    }

    [Fact]
    public void RetiredSuccessors_AllPointToAUserGroup()
    {
        SystemRoleCodes.RetiredSuccessors.ShouldNotBeEmpty();
        SystemRoleCodes.RetiredSuccessors.Values.ShouldAllBe(successor => SystemRoleCodes.UserGroups.Contains(successor));
    }

    [Fact]
    public void RetiredCodes_AreNeitherCustomerNorPlatformSystemRoles()
    {
        foreach (var retired in SystemRoleCodes.RetiredSuccessors.Keys)
        {
            SystemRoleCodes.All.ShouldNotContain(retired);
            SystemRoleCodes.PlatformOnly.ShouldNotContain(retired);
            SystemRoleCodes.ForTenant(isPlatformTenant: true).ShouldNotContain(retired);
        }
    }

    [Theory]
    [InlineData(SystemRoleCodes.Admin, SystemRoleCodes.HR)]
    [InlineData(SystemRoleCodes.HRSpecialist, SystemRoleCodes.HR)]
    [InlineData(SystemRoleCodes.Recruiter, SystemRoleCodes.HR)]
    [InlineData(SystemRoleCodes.Timekeeper, SystemRoleCodes.HR)]
    [InlineData(SystemRoleCodes.ITAdmin, SystemRoleCodes.HR)]
    [InlineData(SystemRoleCodes.Auditor, SystemRoleCodes.HR)]
    [InlineData(SystemRoleCodes.Finance, SystemRoleCodes.PayrollSpecialist)]
    [InlineData(SystemRoleCodes.ExternalAccountant, SystemRoleCodes.PayrollSpecialist)]
    [InlineData(SystemRoleCodes.Interviewer, SystemRoleCodes.Employee)]
    [InlineData(SystemRoleCodes.Intern, SystemRoleCodes.Employee)]
    [InlineData(SystemRoleCodes.Contractor, SystemRoleCodes.Employee)]
    [InlineData(SystemRoleCodes.ApiIntegration, SystemRoleCodes.Employee)]
    public void RetiredCode_MapsToItsSuccessorGroup(string retired, string successor)
    {
        SystemRoleCodes.RetiredSuccessors[retired].ShouldBe(successor);
    }

    [Theory]
    [InlineData("Timekeeper")]
    [InlineData("hrspecialist")]
    [InlineData("ADMIN")]
    public void CreateCustom_WithRetiredSystemCode_FailsWithSystemCodeConflict(string code)
    {
        var result = Role.CreateCustom(Guid.NewGuid(), code, "Eski rol", null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(IdentityDomainErrors.RoleSystemCode);
    }

    [Theory]
    [InlineData(SystemRoleCodes.HR)]
    [InlineData("payrollspecialist")]
    public void CreateCustom_WithCurrentSystemCode_FailsWithSystemCodeConflict(string code)
    {
        var result = Role.CreateCustom(Guid.NewGuid(), code, "Kopya", null);

        result.Error.Code.ShouldBe(IdentityDomainErrors.RoleSystemCode);
    }
}

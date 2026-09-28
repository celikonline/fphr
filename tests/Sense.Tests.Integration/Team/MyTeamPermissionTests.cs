using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Identity.Application.Roles;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Leave.Contracts;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;

namespace Sense.Tests.Integration.Team;

/// <summary>
/// FG-007: the three "Ekibim" keys follow "&lt;module&gt;.my.&lt;name&gt;", so PermissionPatterns.SelfService hands them to every
/// base role - including "Çalışan", where managers now live - without touching SystemRoleDefinitions (SystemRolePermissionSynchronizer
/// spreads them to existing tenants at API startup). No database: pure catalog resolution.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MyTeamPermissionTests
{
    private static readonly IReadOnlyList<Permission> Catalog = [.. EmployeePermissions.All, .. AttendancePermissions.All, .. LeavePermissions.All];

    public static TheoryData<string> BaseRoles => [SystemRoleCodes.Employee, SystemRoleCodes.Manager, SystemRoleCodes.HR, SystemRoleCodes.PayrollSpecialist];

    [Theory]
    [MemberData(nameof(BaseRoles))]
    public void SystemRolePermissionResolver_GrantsTheMyTeamKeys(string roleCode)
    {
        var role = Role.CreateSystem(Guid.NewGuid(), roleCode);

        var keys = SystemRolePermissionResolver.Resolve(role, Catalog).Select(p => p.Permission).ToList();

        keys.ShouldContain(EmployeePermissions.MyTeamKey);
        keys.ShouldContain(AttendancePermissions.MyTeamKey);
        keys.ShouldContain(LeavePermissions.MyTeam.Key);
    }

    [Fact]
    public void EmployeeRole_StillLacksTheDataScopeTeamKeys()
    {
        var keys = SystemRolePermissionResolver.Resolve(Role.CreateSystem(Guid.NewGuid(), SystemRoleCodes.Employee), Catalog)
            .Select(p => p.Permission).ToList();

        keys.ShouldNotContain(AttendancePermissions.TimesheetView.Key);
        keys.ShouldNotContain(LeavePermissions.RequestsRead.Key);
        keys.ShouldNotContain(EmployeePermissions.EmployeesReadKey);
    }
}

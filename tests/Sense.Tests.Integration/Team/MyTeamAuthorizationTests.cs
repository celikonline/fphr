using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Shared.Kernel.Results;
using Sense.Tests.Integration.Identity;
using Shouldly;
using Xunit;
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Tests.Integration.Team;

/// <summary>
/// FG-007 "Ekibim" through the real Identity + Organization + Employee stack (real system roles, AuthorizationBehaviour): a manager in
/// the "Çalışan" group reaches GET employees/me/team via "*.my.*" and sees only their own direct reports; another manager's report
/// never shows up, and a user without any role is refused.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class MyTeamAuthorizationTests : IdentityStackTestBase
{
    [Fact]
    public async Task EmployeeRoleManagers_SeeOnlyTheirOwnDirectReports()
    {
        var tenantId = await SeedTenantAsync("my-team");
        var managerA = await SeedEmployeeWithManagerAsync(tenantId, "Ahmet", "Yılmaz", null);
        var managerB = await SeedEmployeeWithManagerAsync(tenantId, "Burak", "Kaya", null);
        var ayse = await SeedEmployeeWithManagerAsync(tenantId, "Ayşe", "Demir", managerA);
        var deniz = await SeedEmployeeWithManagerAsync(tenantId, "Deniz", "Ak", managerB);
        var elif = await SeedEmployeeWithManagerAsync(tenantId, "Elif", "Can", ayse);
        var userA = await SeedUserAsync(tenantId, "ahmet@acme.test", SystemRoleCodes.Employee, managerA);
        var userB = await SeedUserAsync(tenantId, "burak@acme.test", SystemRoleCodes.Employee, managerB);
        var userDeniz = await SeedUserAsync(tenantId, "deniz@acme.test", SystemRoleCodes.Employee, deniz);

        ActAs(tenantId, userA, managerA);
        var teamA = await ExecuteQueryAsync(new GetMyTeamQuery());
        teamA.IsSuccess.ShouldBeTrue(teamA.IsFailure ? teamA.Error.Code : null);
        teamA.Value.Select(m => m.EmployeeId).ShouldBe([ayse]);
        teamA.Value.Select(m => m.EmployeeId).ShouldNotContain(deniz);
        teamA.Value.Select(m => m.EmployeeId).ShouldNotContain(elif);

        ActAs(tenantId, userB, managerB);
        (await ExecuteQueryAsync(new GetMyTeamQuery())).Value.Select(m => m.EmployeeId).ShouldBe([deniz]);

        ActAs(tenantId, userDeniz, deniz);
        (await ExecuteQueryAsync(new GetMyTeamQuery())).Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task UserWithoutAnyRole_IsForbidden()
    {
        var tenantId = await SeedTenantAsync("my-team-norole");
        var manager = await SeedEmployeeWithManagerAsync(tenantId, "Ahmet", "Yılmaz", null);
        await SeedEmployeeWithManagerAsync(tenantId, "Ayşe", "Demir", manager);
        var userId = await SeedUserAsync(tenantId, "norole@acme.test", roleCode: null, manager);

        ActAs(tenantId, userId, manager);

        ShouldFailWith(await ExecuteQueryAsync(new GetMyTeamQuery()), ErrorCodes.Forbidden, ErrorType.Forbidden);
    }

    private async Task<Guid> SeedEmployeeWithManagerAsync(Guid tenantId, string firstName, string lastName, Guid? managerId)
    {
        var db = Get<EmployeeDbContext>();
        using (TenantContextSetter!.BeginScope(tenantId))
        {
            var employee = EmployeeEntity.Create(
                tenantId, new EmployeeInfo(firstName, lastName, null, null, null, null, null, null), null, null, managerId).Value;
            db.Employees.Add(employee);
            await db.SaveChangesAsync();
            return employee.Id;
        }
    }
}

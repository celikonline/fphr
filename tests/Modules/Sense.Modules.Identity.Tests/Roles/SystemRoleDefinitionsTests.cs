using Sense.Modules.Identity.Application.Roles;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;

namespace Sense.Modules.Identity.Tests.Roles;

public sealed class SystemRoleDefinitionsTests
{
    private static readonly IReadOnlyList<Permission> Catalog =
    [
        .. new[]
        {
            "employee.employees.read", "employee.employees.create", "employee.employees.update", "employee.employees.bulk_update",
            "employee.directory.view", "employee.orgchart.view", "employee.salary.view", "organization.holidays.view", "organization.units.read",
        }.Select(k => new Permission(k, k.Split('.')[0], k, "Test")),
    ];

    [Theory]
    [InlineData(SystemRoleCodes.Employee)]
    [InlineData(SystemRoleCodes.Intern)]
    public void SelfServiceRoles_GetDirectoryOrgChartAndHolidays_ButNotEmployeeRecords(string roleCode)
    {
        var keys = Resolve(roleCode);

        keys.ShouldBe(["employee.directory.view", "employee.orgchart.view", "organization.holidays.view"], ignoreOrder: true);
    }

    [Fact]
    public void HRSpecialist_GetsEmployeeWrites_ButNotSalary()
    {
        var keys = Resolve(SystemRoleCodes.HRSpecialist);

        keys.ShouldContain("employee.employees.update");
        keys.ShouldContain("employee.employees.bulk_update");
        keys.ShouldNotContain("employee.salary.view");
    }

    [Fact]
    public void Manager_GetsViews_ButNotSalary()
    {
        var keys = Resolve(SystemRoleCodes.Manager);

        keys.ShouldContain("employee.directory.view");
        keys.ShouldContain("employee.orgchart.view");
        keys.ShouldNotContain("employee.salary.view");
    }

    private static List<string> Resolve(string roleCode) =>
        SystemRoleDefinitions.Get(roleCode).Resolve(Guid.NewGuid(), Catalog).Select(p => p.Permission).ToList();
}

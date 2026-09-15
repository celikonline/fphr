using Sense.Modules.Employee.Contracts;
using Sense.Modules.Identity.Application.UnitAuthorizations;
using Shouldly;
using Xunit;

namespace Sense.Modules.Identity.Tests.UnitAuthorizations;

public sealed class UnitManagerGrantsTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Grants_AlwaysIncludeBase_SwitchesAddTheirSets_AndCustomReportBuildIsNeverGranted(bool salary, bool reports)
    {
        var grants = UnitManagerGrants.For(salary, reports);

        foreach (var permission in UnitManagerGrants.Base)
        {
            grants.ShouldContain(permission);
        }

        grants.Contains(EmployeePermissions.SalaryViewKey).ShouldBe(salary);
        grants.Contains(UnitManagerGrants.UnitReportsRun).ShouldBe(reports);
        grants.ShouldNotContain(UnitManagerGrants.CustomReportBuild);
        grants.ShouldBeUnique();
    }

    [Fact]
    public void Base_IsTheUnitManagerEmployeeSet()
    {
        UnitManagerGrants.Base.ShouldBe(
            [
                EmployeePermissions.EmployeesReadKey, EmployeePermissions.EmployeesCreateKey, EmployeePermissions.EmployeesUpdateKey,
                EmployeePermissions.DirectoryViewKey, EmployeePermissions.OrgChartViewKey,
            ],
            ignoreOrder: true);
    }
}

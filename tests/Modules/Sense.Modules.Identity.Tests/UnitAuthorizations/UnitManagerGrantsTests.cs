using Sense.Modules.Analytics.Contracts;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Identity.Application.UnitAuthorizations;
using Sense.Modules.Reporting.Contracts;
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

    /// <summary>
    /// G-10-18: the legacy `reports.unit.run` key alone reached no endpoint - Reporting/Analytics check their own
    /// permission strings (`ReportEmployeeSet.LoadAsync`, `GetWorkforceKpisHandler`), so the "kendi birim raporları"
    /// switch must grant those exact keys for a unit manager to actually see scoped reports/KPIs.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnitReportsSwitch_GrantsTheKeysReportingAndAnalyticsActuallyCheck(bool reports)
    {
        var grants = UnitManagerGrants.For(canViewSalaryAndPayments: false, reports);

        grants.Contains(AnalyticsPermissions.KpiView.Key).ShouldBe(reports);
        grants.Contains(ReportingPermissions.CatalogView.Key).ShouldBe(reports);
        grants.Contains(ReportingPermissions.ExecutionsGenerate.Key).ShouldBe(reports);
        grants.Contains(ReportingPermissions.MyExecutions.Key).ShouldBe(reports);
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

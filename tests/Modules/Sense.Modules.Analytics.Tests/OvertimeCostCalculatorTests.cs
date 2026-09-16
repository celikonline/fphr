using Sense.Modules.Analytics.Application.Kpis;
using Sense.Modules.Employee.Contracts;
using Shouldly;
using Xunit;

namespace Sense.Modules.Analytics.Tests;

/// <summary>
/// OvertimeCostCalculator (S-17): pure formula math, no DB/handler - the employee-list wiring the handler would
/// need is blocked on an IEmployeeAnalyticsQueries method that doesn't exist yet (see rapor-be-20260916-1754).
/// </summary>
public sealed class OvertimeCostCalculatorTests
{
    [Fact]
    public void Calculate_AppliesFormulaPerPayFrequency_AndSumsPerCurrency()
    {
        var monthly = new EmployeeCurrentSalary(22_500m, "TRY", EmployeeSalaryType.Gross, EmployeePayFrequency.Monthly, new DateOnly(2026, 1, 1));
        var daily = new EmployeeCurrentSalary(750m, "TRY", EmployeeSalaryType.Gross, EmployeePayFrequency.Daily, new DateOnly(2026, 1, 1));
        var hourly = new EmployeeCurrentSalary(20m, "USD", EmployeeSalaryType.Gross, EmployeePayFrequency.Hourly, new DateOnly(2026, 1, 1));

        var inputs = new[]
        {
            new OvertimeCostInput(Guid.NewGuid(), OvertimeHours: 10m, monthly), // 22500/225 = 100/h * 1.5 * 10 = 1500
            new OvertimeCostInput(Guid.NewGuid(), OvertimeHours: 4m, daily),    // 750/7.5 = 100/h * 1.5 * 4 = 600
            new OvertimeCostInput(Guid.NewGuid(), OvertimeHours: 5m, hourly),   // 20/h * 1.5 * 5 = 150
        };

        var result = OvertimeCostCalculator.Calculate(inputs);

        result.EmployeeCount.ShouldBe(3);
        result.ExcludedEmployeeCount.ShouldBe(0);
        result.Totals.ShouldContain(t => t.Currency == "TRY" && t.Amount == 2100m); // 1500 + 600, no cross-currency conversion
        result.Totals.ShouldContain(t => t.Currency == "USD" && t.Amount == 150m);
        result.Totals.Count.ShouldBe(2);
    }

    [Fact]
    public void Calculate_Excludes_NoSalaryRecord_AndUndefinedPayFrequencyConversion()
    {
        var weekly = new EmployeeCurrentSalary(5_000m, "TRY", EmployeeSalaryType.Gross, EmployeePayFrequency.Weekly, new DateOnly(2026, 1, 1));
        var monthly = new EmployeeCurrentSalary(20_000m, "TRY", EmployeeSalaryType.Gross, EmployeePayFrequency.Monthly, new DateOnly(2026, 1, 1));

        var inputs = new[]
        {
            new OvertimeCostInput(Guid.NewGuid(), OvertimeHours: 10m, Salary: null), // no salary record at all
            new OvertimeCostInput(Guid.NewGuid(), OvertimeHours: 10m, weekly),       // Weekly has no defined hourly conversion
            new OvertimeCostInput(Guid.NewGuid(), OvertimeHours: 9m, monthly),       // 20000/225 = 88.888...9 -> included
        };

        var result = OvertimeCostCalculator.Calculate(inputs);

        result.EmployeeCount.ShouldBe(1);
        result.ExcludedEmployeeCount.ShouldBe(2);
        result.Totals.ShouldHaveSingleItem();
        result.Totals[0].Currency.ShouldBe("TRY");
    }
}

namespace Sense.Modules.Payroll.Domain.Unions;

/// <summary>Pure computation of one period's union/solidarity dues amount (11.3.7 "aidat kuralı") from an employee's
/// current <see cref="EmployeeUnionMembership"/>, if any applies. No DB access; the Application layer resolves the
/// current membership and calls this from <c>PayrollPeriodCalculationService</c> before building
/// <see cref="Calculation.PayrollCalculationInput"/> (mirrors how meal/expense amounts are resolved outside the
/// calculator itself).</summary>
public static class UnionDuesCalculator
{
    public static decimal Calculate(EmployeeUnionMembership? membership, decimal grossMonthlySalary, int calendarDaysInMonth, DateOnly periodMonthStart)
    {
        if (membership is null || !membership.AppliesDuesForPeriod(periodMonthStart))
        {
            return 0m;
        }

        var amount = membership.DuesRule switch
        {
            UnionDuesRule.PercentOfGross => grossMonthlySalary * membership.DuesValue,
            UnionDuesRule.FixedAmount => membership.DuesValue,
            UnionDuesRule.DailyWage => (grossMonthlySalary / calendarDaysInMonth) * membership.DuesValue,
            _ => throw new NotSupportedException($"Union dues rule '{membership.DuesRule}' is not a known value."),
        };

        return decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
    }
}

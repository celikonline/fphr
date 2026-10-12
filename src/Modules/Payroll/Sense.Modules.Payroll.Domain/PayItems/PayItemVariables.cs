using Sense.Modules.Payroll.Domain.Calculation;
using Sense.Modules.Payroll.Domain.PayItems.Formulas;

namespace Sense.Modules.Payroll.Domain.PayItems;

/// <summary>Facts about the employee-month that are not part of <see cref="PayrollCalculationInput"/> but formulas may read.</summary>
/// <param name="NetTarget">Agreed net of a net-wage employee; 0 for gross-wage employees.</param>
/// <param name="MissingDays">Days without pay or work (absence), 0 when none.</param>
/// <param name="SeniorityYears">Whole years from the hire date to the period end; 0 when the hire date is unknown.</param>
/// <param name="OvertimeHours">Approved cash-paid overtime hours of the month (all rates).</param>
public sealed record PayItemVariableFacts(decimal NetTarget, int MissingDays, int SeniorityYears, decimal OvertimeHours);

/// <summary>
/// Pure builder of the variable values a pay-item formula can read (PAY-FORMULA-PLAN "İzinli değişkenler"). All values come from
/// the inputs known BEFORE the pay items are added, so a formula can never depend on another pay item or on its own result.
/// </summary>
public static class PayItemVariables
{
    /// <summary>
    /// Values keyed by <see cref="PayItemVariableNames"/>. <paramref name="baseInput"/> is the month's calculation input WITHOUT
    /// any pay item (its <see cref="PayrollCalculator.BaseSalary"/> is <c>brut</c>); <paramref name="value"/> is the assignment's
    /// <c>deger</c> (null = the key is absent, so a formula that uses it fails as an unknown variable).
    /// </summary>
    public static IReadOnlyDictionary<string, decimal> Build(
        PayrollCalculationInput baseInput, PayrollLegislationSnapshot snapshot, PayItemVariableFacts facts, decimal? value)
    {
        var monthly = baseInput.GrossMonthlySalary;
        var variables = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            [PayItemVariableNames.Gross] = PayrollCalculator.BaseSalary(baseInput),
            [PayItemVariableNames.MonthlyWage] = monthly,
            [PayItemVariableNames.NetTarget] = facts.NetTarget,
            [PayItemVariableNames.DailyWage] = monthly / EmploymentProration.PayrollMonthDays,
            [PayItemVariableNames.HourlyWage] = monthly / PeriodWageMath.MonthlyWorkingHours,
            [PayItemVariableNames.WorkedDays] = baseInput.WorkedDays,
            [PayItemVariableNames.PaidDays] = PaidDays(baseInput),
            [PayItemVariableNames.CalendarDays] = baseInput.CalendarDaysInMonth,
            [PayItemVariableNames.MissingDays] = facts.MissingDays,
            [PayItemVariableNames.SeniorityYears] = facts.SeniorityYears,
            [PayItemVariableNames.MinimumWage] = snapshot.MinimumWageGross,
            [PayItemVariableNames.OvertimeHours] = facts.OvertimeHours,
            [PayItemVariableNames.Month] = baseInput.AsOfDate.Month,
        };
        if (value is { } v)
        {
            variables[PayItemVariableNames.Value] = v;
        }

        return variables;
    }

    /// <summary>Whole years between the hire date and the period end (0 if unknown, in the future or under a year).</summary>
    public static int SeniorityYears(DateOnly? hireDate, DateOnly periodEnd)
    {
        if (hireDate is not { } hired || hired > periodEnd)
        {
            return 0;
        }

        var years = periodEnd.Year - hired.Year;
        if (periodEnd.Month < hired.Month || (periodEnd.Month == hired.Month && periodEnd.Day < hired.Day))
        {
            years--;
        }

        return Math.Max(0, years);
    }

    private static int PaidDays(PayrollCalculationInput input) =>
        input.Wage?.PaidDays
        ?? (input.ProratedPaidDays is { } days and > 0 and < EmploymentProration.PayrollMonthDays ? days : EmploymentProration.PayrollMonthDays);
}

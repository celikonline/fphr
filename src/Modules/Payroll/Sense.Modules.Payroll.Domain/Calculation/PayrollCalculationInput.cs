namespace Sense.Modules.Payroll.Domain.Calculation;

/// <summary>
/// Plain input for <see cref="PayrollCalculator.Calculate"/>: one monthly-salaried employee, one period.
/// </summary>
/// <param name="GrossMonthlySalary">Maktu (aylık) brüt ücret; WageBasis=Monthly only in this slice (11.3.7 "Ücret esası").</param>
/// <param name="WorkedDays">
/// TODO(Attendance seam): until the Attendance module exists (roadmap #23, Faz 2) there is no missing-day/timesheet
/// input, so callers pass <see cref="CalendarDaysInMonth"/> here (full worked month, no absences - explicit user
/// decision, faz3-durum-takibi.md). The parameter is kept so wiring real Attendance data later only changes the
/// caller, not this calculator: a real <c>IAttendanceInputProvider</c>-shaped port would just resolve this value.
/// </param>
/// <param name="CalendarDaysInMonth">Days in the period's calendar month (used for daily-rate display and meal exemption pro-ration).</param>
/// <param name="MealVariant">Which of the 4 meal benefit shapes applies (11.3.07-bordro.md "Yemek ödemesi 4 varyantı"); all are implemented (P2).</param>
/// <param name="MealDailyAmount">Meal benefit paid per worked day; ignored (treated as 0) unless <paramref name="MealVariant"/> requires one (Cash/MealCardOnly/MealAndMarketCard).</param>
/// <param name="PriorCumulativeGvBase">Sum of GV_BASE from earlier months in the same calendar year (0 for a first-month/single-month case).</param>
/// <param name="AsOfDate">Reference date for resolving which LegislationParameter row applies (ADR 0008); normally the period's month.</param>
/// <param name="ExpenseReimbursement">
/// TB-7 Expense köprüsü (faz-07 tur 1 madde 3, S-13 <c>IApprovedExpensePayoutReader</c>): sum of the employee's
/// Approved-and-not-yet-paid <c>ExpenseClaim</c> totals for this period's month. Tax-free by definition (an expense
/// reimbursement, not pay) - it is added straight to NET and never touches SGK_BASE/GV_BASE/INCOME_TAX/STAMP_TAX,
/// unlike the meal benefit's SGK/GV-excess handling. Defaults to 0 so existing callers/golden-set tests are
/// unaffected until a caller wires <c>IExpensePayoutReader</c>.
/// </param>
public sealed record PayrollCalculationInput(
    decimal GrossMonthlySalary,
    int WorkedDays,
    int CalendarDaysInMonth,
    MealBenefitVariant MealVariant,
    decimal MealDailyAmount,
    decimal PriorCumulativeGvBase,
    DateOnly AsOfDate,
    decimal ExpenseReimbursement = 0m);

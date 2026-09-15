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
/// <param name="MealVariant">Only <see cref="MealBenefitVariant.None"/> and <see cref="MealBenefitVariant.Cash"/> are implemented this slice.</param>
/// <param name="MealDailyAmount">Cash meal benefit paid per worked day; ignored unless <paramref name="MealVariant"/> is Cash.</param>
/// <param name="PriorCumulativeGvBase">Sum of GV_BASE from earlier months in the same calendar year (0 for a first-month/single-month case).</param>
/// <param name="AsOfDate">Reference date for resolving which LegislationParameter row applies (ADR 0008); normally the period's month.</param>
public sealed record PayrollCalculationInput(
    decimal GrossMonthlySalary,
    int WorkedDays,
    int CalendarDaysInMonth,
    MealBenefitVariant MealVariant,
    decimal MealDailyAmount,
    decimal PriorCumulativeGvBase,
    DateOnly AsOfDate);

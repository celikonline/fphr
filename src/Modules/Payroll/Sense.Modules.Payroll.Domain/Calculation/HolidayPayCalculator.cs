using Sense.Shared.Kernel;

namespace Sense.Modules.Payroll.Domain.Calculation;

/// <summary>
/// Hafta tatili (4857 md.46) ve genel/resmî tatil (md.47) ücreti, senseik-019 2d. Aylık ücretli bir çalışan için
/// tatil günü zaten ücrete dahildir (çalışılmasa da 1 günlük ücret hak edilir); bu hesaplayıcı yalnızca tatilde
/// FİİLEN ÇALIŞILAN günler için hak edilen EK ücreti üretir (md.46/47: çalışılırsa +1 yevmiye, yani günlük ücretin
/// %100 fazlası - toplamda 2 yevmiye). Puantajdan gelen "hangi gün hafta tatili/resmî tatildi ve kim çalıştı"
/// bilgisi senseik-014'ün (Puantaj motoru) sorumluluğundadır; bkz. handoff.
/// </summary>
public static class HolidayPayCalculator
{
    public static decimal DailyRate(decimal grossMonthlySalary, int calendarDaysInMonth)
    {
        Guard.Against(grossMonthlySalary <= 0m, "grossMonthlySalary must be positive.");
        Guard.Positive(calendarDaysInMonth, nameof(calendarDaysInMonth));
        return Round(grossMonthlySalary / calendarDaysInMonth);
    }

    /// <summary>Toplam ek tatil ücreti (vergiye/SGK'ya tabi, PayrollCalculationInput.AdditionalTaxableEarnings'e eklenir).</summary>
    public static decimal Calculate(decimal grossMonthlySalary, int calendarDaysInMonth, int weeklyRestDaysWorked, int publicHolidayDaysWorked)
    {
        Guard.Against(weeklyRestDaysWorked < 0, "weeklyRestDaysWorked cannot be negative.");
        Guard.Against(publicHolidayDaysWorked < 0, "publicHolidayDaysWorked cannot be negative.");

        var dailyRate = DailyRate(grossMonthlySalary, calendarDaysInMonth);
        return Round(dailyRate * (weeklyRestDaysWorked + publicHolidayDaysWorked));
    }

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}

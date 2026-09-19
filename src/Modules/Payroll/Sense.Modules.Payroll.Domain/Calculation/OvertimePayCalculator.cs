using Sense.Shared.Kernel;

namespace Sense.Modules.Payroll.Domain.Calculation;

/// <summary>
/// Fazla mesai ücreti (4857 s. İş Kanunu md.41, senseik-019 2d): saatlik ücret = aylık brüt ücret / aylık çalışma
/// saati esası (Leave.Domain.Overtime.OvertimeMath'in 7,5 saat/gün esasıyla aynı, 30 gün * 7,5 saat = 225 saat).
/// "Fazla çalışma" (haftalık 45 saati aşan kısım) %50 zamlı, "fazla sürelerle çalışma" (haftalık çalışma süresinin
/// sözleşmeyle 45 saatin altında belirlendiği işyerlerinde 45 saate kadar olan kısım) %25 zamlı ödenir.
/// Bu hesaplayıcı yalnızca TL tutarını üretir - yıllık 270 saat üst sınırı sayacı ve saatlerin 25/50 kategorisine
/// ayrıştırılması senseik-012'nin (Attendance/Leave "Fazla mesai motoru") sorumluluğundadır; bkz. handoff.
/// </summary>
public static class OvertimePayCalculator
{
    /// <summary>4857 md.63 (haftalık 45 saat / 6 gün = günde 7,5 saat) * 30 gün - Leave.Domain.Overtime.OvertimeMath ile aynı esas.</summary>
    public const int MonthlyWorkingHoursDivisor = 225;

    public static decimal HourlyRate(decimal grossMonthlySalary)
    {
        Guard.Against(grossMonthlySalary <= 0m, "grossMonthlySalary must be positive.");
        return Round(grossMonthlySalary / MonthlyWorkingHoursDivisor);
    }

    /// <summary>Toplam fazla mesai ücreti (vergiye/SGK'ya tabi, PayrollCalculationInput.AdditionalTaxableEarnings'e eklenir).</summary>
    public static decimal Calculate(decimal grossMonthlySalary, decimal overtimeHours25, decimal overtimeHours50)
    {
        Guard.Against(overtimeHours25 < 0m, "overtimeHours25 cannot be negative.");
        Guard.Against(overtimeHours50 < 0m, "overtimeHours50 cannot be negative.");

        var hourlyRate = HourlyRate(grossMonthlySalary);
        var pay25 = Round(overtimeHours25 * hourlyRate * 1.25m);
        var pay50 = Round(overtimeHours50 * hourlyRate * 1.50m);
        return pay25 + pay50;
    }

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}

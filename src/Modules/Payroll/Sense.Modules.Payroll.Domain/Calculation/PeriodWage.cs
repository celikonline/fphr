namespace Sense.Modules.Payroll.Domain.Calculation;

/// <summary>
/// Günlük/saatlik ücretli (<see cref="WageBasis.Daily"/> / <see cref="WageBasis.Hourly"/>) çalışanın dönem ücreti:
/// brüt = birim ücret x ücretli gün/saat. Aylık ücretlide null.
/// </summary>
/// <param name="Basis">Yalnız Daily veya Hourly.</param>
/// <param name="UnitRate">Günlük brüt ücret (Daily) ya da saat brütü (Hourly).</param>
/// <param name="PaidDays">Ücretli gün (30 gün esası; kısmi ay ve eksik gün düşülmüş), 1..30.</param>
/// <param name="PaidHours">Onaylı puantajdan çözülen normal ücretli saat; null yalnız önceki hesap girdileriyle uyumluluk içindir.</param>
/// <param name="NetUnitRate">Net ücret anlaşmasının orijinal günlük/saatlik tutarı; brütleştirme izinde gösterilir.</param>
public sealed record PeriodWage(WageBasis Basis, decimal UnitRate, int PaidDays, decimal? PaidHours = null, decimal? NetUnitRate = null);

/// <summary>Günlük/saatlik ücretin aylık karşılığı ve dönem brütü. Saatlik ücrette onaylı puantajdan çözülen ücretli saat önceliklidir.</summary>
public static class PeriodWageMath
{
    /// <summary>4857 md.63 (haftalık 45 saat / 6 gün) - OvertimePayCalculator.MonthlyWorkingHoursDivisor ile aynı esas.</summary>
    public const decimal DailyWorkingHours = 7.5m;

    /// <summary>Aylık çalışma saati esası: 30 gün x 7,5 saat = 225.</summary>
    public const decimal MonthlyWorkingHours = EmploymentProration.PayrollMonthDays * DailyWorkingHours;

    /// <summary>Daily/Hourly dışındaki esaslar bu hesapta desteklenmez.</summary>
    public static bool IsSupported(WageBasis basis) => basis is WageBasis.Daily or WageBasis.Hourly;

    /// <summary>Aylık karşılık: günlük x 30, saatlik x 225 (fazla mesai, tatil ücreti, sendika aidatı için).</summary>
    public static decimal MonthlyEquivalent(WageBasis basis, decimal unitRate) => basis switch
    {
        WageBasis.Daily => unitRate * EmploymentProration.PayrollMonthDays,
        WageBasis.Hourly => unitRate * MonthlyWorkingHours,
        _ => throw new NotSupportedException($"Wage basis '{basis}' has no daily/hourly monthly equivalent."),
    };

    /// <summary>Dönem brütü: günlük x ücretli gün; saatlik x 7,5 x ücretli gün.</summary>
    public static decimal PeriodGross(WageBasis basis, decimal unitRate, int paidDays, decimal? paidHours = null) => basis switch
    {
        WageBasis.Daily => decimal.Round(unitRate * paidDays, 2, MidpointRounding.AwayFromZero),
        WageBasis.Hourly => decimal.Round(unitRate * (paidHours ?? DailyWorkingHours * paidDays), 2, MidpointRounding.AwayFromZero),
        _ => throw new NotSupportedException($"Wage basis '{basis}' has no daily/hourly period gross."),
    };
}

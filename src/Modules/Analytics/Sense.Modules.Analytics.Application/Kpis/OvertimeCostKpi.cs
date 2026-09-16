using Sense.Modules.Employee.Contracts;

namespace Sense.Modules.Analytics.Application.Kpis;

/// <summary>
/// S-17 fazla mesai maliyeti hesabının bir çalışan girdisi: dönem içindeki fazla mesai saati (Attendance.Contracts
/// IAttendanceMonthlySummaryReader.OvertimeHours'tan) ve güncel maaş kaydı
/// (<see cref="IEmployeeSalaryLookup.GetCurrentSalaryAsync"/>'ten). Maaşı olmayan çalışan için <see cref="Salary"/>
/// null geçilir - hesaba katılmaz, <see cref="OvertimeCostResult.ExcludedEmployeeCount"/>'a sayılır.
/// </summary>
public sealed record OvertimeCostInput(Guid EmployeeId, decimal OvertimeHours, EmployeeCurrentSalary? Salary);

/// <summary>One currency's total estimated overtime cost for the requested period (no cross-currency conversion).</summary>
public sealed record OvertimeCostTotal(string Currency, decimal Amount);

/// <summary>
/// S-17 sonucu: para birimi başına ayrı toplam (<see cref="Totals"/>, çevrim yapılmaz), hesaba giren
/// (<see cref="EmployeeCount"/>) ve dönüşüm kuralı tanımlı olmadığı için hariç tutulan
/// (<see cref="ExcludedEmployeeCount"/>) çalışan sayısı.
/// </summary>
public sealed record OvertimeCostResult(IReadOnlyList<OvertimeCostTotal> Totals, int EmployeeCount, int ExcludedEmployeeCount);

/// <summary>
/// Fazla mesai maliyeti tahmini (S-17; PO kararı, tur 1 açılışı 2026-09-16 - 4857 sayılı İş Kanunu md.41 "fazla
/// çalışma ücreti" normal saat ücretinin %50 fazlasıyla ödenir; md.63 haftalık azami çalışma süresinden türeyen
/// aylık ~225 saat esası yaygın bordro uygulamasıdır - bkz. <c>PayrollCalculator</c>, o motor saatlik ücret
/// hesaplamaz, bu yüzden burada ayrı bir dönüşüm kuralı tanımlanır). Yalnızca dönüşüm kuralı tanımlı olan ödeme
/// düzenleri (<see cref="EmployeePayFrequency.Monthly"/>, <see cref="EmployeePayFrequency.Daily"/>,
/// <see cref="EmployeePayFrequency.Hourly"/>) hesaba girer; <see cref="EmployeePayFrequency.Weekly"/>/
/// <see cref="EmployeePayFrequency.Yearly"/> ve maaş kaydı olmayan çalışanlar <see cref="OvertimeCostResult.ExcludedEmployeeCount"/>'a
/// sayılır. Para birimleri arasında çevrim yapılmaz - her biri kendi toplamında görünür.
/// </summary>
public static class OvertimeCostCalculator
{
    /// <summary>Aylık maaşı saatlik ücrete bölen ay içi saat sayısı (4857 md.63 haftalık azami 45 saat esaslı, aylık ~225 saat yaygın kabul).</summary>
    public const decimal MonthlyHoursDivisor = 225m;

    /// <summary>Günlük maaşı saatlik ücrete bölen saat sayısı (günlük 7,5 saatlik olağan çalışma süresi esası).</summary>
    public const decimal DailyHoursDivisor = 7.5m;

    /// <summary>Fazla çalışma zam oranı (4857 md.41: normal saat ücretinin %50 fazlasıyla ödenir).</summary>
    public const decimal OvertimeMultiplier = 1.5m;

    public static OvertimeCostResult Calculate(IReadOnlyList<OvertimeCostInput> inputs)
    {
        var totalsByCurrency = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var includedCount = 0;
        var excludedCount = 0;

        foreach (var input in inputs)
        {
            var hourlyRate = TryGetHourlyRate(input.Salary);
            if (hourlyRate is null)
            {
                excludedCount++;
                continue;
            }

            includedCount++;
            var cost = Math.Round(input.OvertimeHours * hourlyRate.Value * OvertimeMultiplier, 2, MidpointRounding.AwayFromZero);
            var currency = input.Salary!.Currency;
            totalsByCurrency[currency] = totalsByCurrency.GetValueOrDefault(currency) + cost;
        }

        var totals = totalsByCurrency
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new OvertimeCostTotal(kv.Key, kv.Value))
            .ToList();

        return new OvertimeCostResult(totals, includedCount, excludedCount);
    }

    private static decimal? TryGetHourlyRate(EmployeeCurrentSalary? salary)
    {
        if (salary is null)
        {
            return null;
        }

        return salary.PayFrequency switch
        {
            EmployeePayFrequency.Monthly => salary.Amount / MonthlyHoursDivisor,
            EmployeePayFrequency.Daily => salary.Amount / DailyHoursDivisor,
            EmployeePayFrequency.Hourly => salary.Amount,
            _ => null,
        };
    }
}

using Sense.Modules.Analytics.Contracts;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

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

/// <summary>S-17 uç sözleşmesi (tur içi ek iş md.7, 2026-09-16 18:13): <see cref="OvertimeCostResult"/>'a dönem etiketi ekler.</summary>
public sealed record OvertimeCostKpiDto(int Year, int Month, IReadOnlyList<OvertimeCostTotal> Totals, int EmployeeCount, int ExcludedEmployeeCount);

/// <summary>
/// S-17 fazla mesai maliyeti ucu (roadmap #99). Ücret hassas alan olduğu için iki izin birden gerekir:
/// <see cref="AnalyticsPermissions.KpiView"/> (KPI ekranını görme) VE <see cref="EmployeePermissions.SalaryViewKey"/>
/// (maaş verisine erişim) - <see cref="RequiresPermissionAttribute"/> AllowMultiple ile AND semantiğinde
/// (bkz. AuthorizationBehaviour), ikisinden biri eksikse 403.
/// </summary>
[RequiresPermission("analytics.kpi.view")]
[RequiresPermission(EmployeePermissions.SalaryViewKey)]
public sealed record GetOvertimeCostKpiQuery(int Year, int Month, Guid? OrgUnitId = null) : IQuery<OvertimeCostKpiDto>;

/// <summary>
/// G-10-17: yalnız sorgu anında <c>Active</c> ve kapsam dahilindeki çalışanları sayar; dönem içinde
/// ayrılan/askıya alınan çalışanın o dönemdeki verisi dahil edilmez (dönem-farkındalıklı sorgu backlog B-10-03).
/// </summary>
public sealed class GetOvertimeCostKpiHandler(
    ITenantContext tenant,
    IAttendanceMonthlySummaryReader attendanceSummaries,
    IEmployeeSalaryLookup salaryLookup,
    IEmployeeDirectory employeeDirectory,
    IDataScopeService dataScope)
    : IQueryHandler<GetOvertimeCostKpiQuery, OvertimeCostKpiDto>
{
    public async Task<Result<OvertimeCostKpiDto>> Handle(GetOvertimeCostKpiQuery query, CancellationToken cancellationToken)
    {
        // Same IDataScopeService pattern as the workforce/leave-usage KPIs: null = Company scope (unrestricted),
        // a non-null set narrows to exactly what the caller may see. Employee.Contracts' IEmployeeDirectory already
        // exposes "every active employee"/"active employees in these units" (added for the Payroll period run,
        // 11.3.07 P3) - the Company-scope caller reuses it instead of a new port (found while wiring ADR-0014;
        // superseded, see rapor-be-20260916-1835).
        var visibleEmployeeIds = await dataScope.GetVisibleEmployeeIdsAsync(AnalyticsPermissions.KpiView.Key, cancellationToken);

        IReadOnlyList<Guid> employeeIds;
        if (visibleEmployeeIds is null)
        {
            employeeIds = query.OrgUnitId is { } orgUnitId
                ? await ListActiveEmployeeIdsInUnitAsync(orgUnitId, cancellationToken)
                : await employeeDirectory.ListActiveEmployeeIdsAsync(tenant.TenantId, cancellationToken);
        }
        else if (query.OrgUnitId is { } orgUnitId)
        {
            var activeOrgUnitEmployeeIds = await ListActiveEmployeeIdsInUnitAsync(orgUnitId, cancellationToken);
            employeeIds = activeOrgUnitEmployeeIds.Where(visibleEmployeeIds.Contains).ToList();
        }
        else
        {
            employeeIds = visibleEmployeeIds.ToList();
        }

        if (employeeIds.Count == 0)
        {
            return Result.Success(new OvertimeCostKpiDto(query.Year, query.Month, [], 0, 0));
        }

        var summaries = await attendanceSummaries.GetAsync(tenant.TenantId, query.Year, query.Month, employeeIds, cancellationToken);

        // G-10-19: one batched salary lookup instead of one round trip per employee with an attendance summary.
        var salariesByEmployeeId = await salaryLookup.GetCurrentSalariesAsync(
            tenant.TenantId, summaries.Select(s => s.EmployeeId).ToList(), cancellationToken);

        var inputs = summaries
            .Select(summary => new OvertimeCostInput(summary.EmployeeId, summary.OvertimeHours, salariesByEmployeeId.GetValueOrDefault(summary.EmployeeId)))
            .ToList();

        var result = OvertimeCostCalculator.Calculate(inputs);
        return Result.Success(new OvertimeCostKpiDto(query.Year, query.Month, result.Totals, result.EmployeeCount, result.ExcludedEmployeeCount));
    }

    /// <summary>
    /// G-10-16: <see cref="IEmployeeDirectory.ListActiveEmployeeIdsInOrgUnitsAsync"/> is broader than "Active"
    /// (it excludes only <c>Terminated</c>, so <c>Draft</c>/<c>Suspended</c> employees are included) - unlike
    /// <see cref="IEmployeeDirectory.ListActiveEmployeeIdsAsync"/>. Intersecting with the latter keeps the
    /// org-unit branch on the same "Active" set as the company-wide branch, so per-unit totals stay comparable.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ListActiveEmployeeIdsInUnitAsync(Guid orgUnitId, CancellationToken cancellationToken)
    {
        var orgUnitEmployeeIds = await employeeDirectory.ListActiveEmployeeIdsInOrgUnitsAsync(tenant.TenantId, [orgUnitId], cancellationToken);
        var activeEmployeeIds = await employeeDirectory.ListActiveEmployeeIdsAsync(tenant.TenantId, cancellationToken);
        var active = new HashSet<Guid>(activeEmployeeIds);
        return orgUnitEmployeeIds.Where(active.Contains).ToList();
    }
}

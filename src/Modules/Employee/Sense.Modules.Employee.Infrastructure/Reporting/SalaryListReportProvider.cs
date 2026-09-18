using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.Export;

namespace Sense.Modules.Employee.Infrastructure.Reporting;

/// <summary>
/// Kolay İK "Finans raporu"nun maaş yarısı (Raporlar → Finans; part8 #32, 11.3.12): çalışan + kariyer bilgileri,
/// bugün geçerli maaş kaydı (tutar, para birimi, Net/Brüt, ödeme düzeni, geçerlilik), yol/yemek yardımı. Yalnız
/// employee.salary.view taşıyanlara listelenir - birim yöneticisinde "maaş anahtarı" kapalıysa rapor kataloğa hiç
/// düşmez, Kolay İK ile aynı davranış. Dönem ödemeleri (avans/harcama/FM) Expense modülünün ayrı bir katkısı olarak
/// planlıdır; bu rapor onları içermez.
/// </summary>
public sealed class SalaryListReportProvider(
    EmployeeDbContext context,
    IDataScopeService dataScope,
    IOrgUnitLookup orgUnits,
    IEmployeeReportReader employees,
    IReportExporter exporter,
    IStringLocalizer<SharedResource> localizer,
    TimeProvider clock) : IReportProvider
{
    public const string ReportKey = "finance.salaries";

    public string Key => ReportKey;

    public string CategoryKey => ReportCategories.Finance;

    public string? RequiredPermission => EmployeePermissions.SalaryViewKey;

    public IReadOnlyList<ReportParameterDescriptor> Parameters { get; } =
    [
        new(ReportParameterKeys.OrgUnitIds, ReportParameterKind.OrgUnits),
    ];

    public async Task<ReportFileResult> GenerateAsync(Guid tenantId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        // Row set is scoped by the salary permission itself, so a Department-scoped salary viewer only exports their unit.
        var (rows, hierarchy) = await new ReportEmployeeSet(dataScope, orgUnits, employees)
            .LoadAsync(tenantId, EmployeePermissions.SalaryViewKey, parameters, cancellationToken);

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var ids = rows.Select(r => r.EmployeeId).ToList();
        var currentSalaries = ids.Count == 0
            ? new Dictionary<Guid, SalaryRecord>()
            : (await context.Set<SalaryRecord>().AsNoTracking()
                .Where(r => r.TenantId == tenantId && ids.Contains(r.EmployeeId) && r.EffectiveFrom <= today)
                .ToListAsync(cancellationToken))
                .GroupBy(r => r.EmployeeId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.EffectiveFrom).First());

        var columns = new List<ExportColumn>
        {
            new(localizer.Column("firstName")),
            new(localizer.Column("lastName")),
        };
        columns.AddRange(hierarchy.Levels.Select(l => new ExportColumn(l.Name)));
        columns.Add(new ExportColumn(localizer.Column("hireDate"), ExportColumnType.Date));
        columns.Add(new ExportColumn(localizer.Column("status")));
        columns.Add(new ExportColumn(localizer.Column("salaryAmount"), ExportColumnType.Number));
        columns.Add(new ExportColumn(localizer.Column("currency")));
        columns.Add(new ExportColumn(localizer.Column("salaryType")));
        columns.Add(new ExportColumn(localizer.Column("payFrequency")));
        columns.Add(new ExportColumn(localizer.Column("effectiveFrom"), ExportColumnType.Date));
        columns.Add(new ExportColumn(localizer.Column("transportAllowance"), ExportColumnType.Number));
        columns.Add(new ExportColumn(localizer.Column("mealAllowance"), ExportColumnType.Number));

        var data = new List<IReadOnlyList<object?>>(rows.Count);
        foreach (var row in rows)
        {
            var salary = currentSalaries.GetValueOrDefault(row.EmployeeId);
            var cells = new List<object?> { row.FirstName, row.LastName };
            cells.AddRange(hierarchy.LevelValues(row.OrgAssignments, row.OrgUnitId));
            cells.Add(row.HireDate);
            cells.Add(localizer.EnumText("EmployeeStatus", row.Status));
            cells.Add(salary?.Amount);
            cells.Add(salary?.Currency);
            cells.Add(localizer.EnumText(salary?.SalaryType));
            cells.Add(localizer.EnumText(salary?.PayFrequency));
            cells.Add(salary?.EffectiveFrom);
            cells.Add(salary?.TransportAllowance);
            cells.Add(salary?.MealAllowance);
            data.Add(cells);
        }

        var sheet = localizer.Sheet("salaries");
        var file = exporter.ExportToExcel($"{sheet} {today:yyyy-MM-dd}.xlsx", [new ExportTable(sheet, columns, data)], cancellationToken);
        return new ReportFileResult(file.FileName, file.ContentType, file.Content);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Balances;
using Sense.Modules.Leave.Domain.Ledger;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Modules.Leave.Infrastructure.Persistence;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.Export;

namespace Sense.Modules.Leave.Infrastructure.Reporting;

/// <summary>
/// Kolay İK "Çalışan izin bakiye raporu" - kısa set (roadmap #44, 11.3.12 `leave.balance.summary`, destek #5953739):
/// Ad, Soyad, kariyer kolonları (kiracı seviyeleri), İşe giriş tarihi, İzne esas hak ediş tarihi, Kalan, Toplam hak
/// edilen, Devir kayıpları, Kullanılan + güncel dönem ve ileri tarihli onaylılar. Yalnız yıllık izni (Code=Annual)
/// kapsar; rapor alındığı güne göre hesaplanır (AsOf = bugün). Kural setinde yıllık izin türü olmayan çalışanın satırı
/// kalır, izin kolonları boş gelir. Filtreler: Birimler, işten ayrılanları dahil et. Satır kümesi çağıranın
/// leave.balances.read kapsamıdır. Senkron Excel; ayrıntılı 25 kolonlu `leave.balance.detail` (tarih aralığı
/// kolonları, asenkron) ayrı bir kalem olarak beklemektedir.
/// </summary>
public sealed class LeaveBalanceSummaryReportProvider(
    LeaveDbContext context,
    IDataScopeService dataScope,
    IOrgUnitLookup orgUnits,
    IEmployeeReportReader employees,
    IReportExporter exporter,
    IStringLocalizer<SharedResource> localizer,
    TimeProvider clock) : IReportProvider
{
    public const string ReportKey = "leave.balance.summary";

    public string Key => ReportKey;

    public string CategoryKey => ReportCategories.Administrative;

    public string? RequiredPermission => LeavePermissions.BalancesRead.Key;

    public IReadOnlyList<ReportParameterDescriptor> Parameters { get; } =
    [
        new(ReportParameterKeys.OrgUnitIds, ReportParameterKind.OrgUnits),
        new(ReportParameterKeys.IncludeTerminated, ReportParameterKind.Boolean),
    ];

    public async Task<ReportFileResult> GenerateAsync(Guid tenantId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var (rows, hierarchy) = await new ReportEmployeeSet(dataScope, orgUnits, employees)
            .LoadAsync(tenantId, LeavePermissions.BalancesRead.Key, parameters, cancellationToken);

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var ids = rows.Select(r => r.EmployeeId).ToList();

        var profiles = ids.Count == 0
            ? []
            : await context.Profiles.AsNoTracking()
                .Where(p => p.TenantId == tenantId && ids.Contains(p.EmployeeId))
                .ToListAsync(cancellationToken);
        var profileByEmployee = profiles.GroupBy(p => p.EmployeeId).ToDictionary(g => g.Key, g => g.First());

        // A tenant may have several rule sets, each with its own Annual-coded type; resolve per profile.
        var ruleSets = await context.RuleSets.AsNoTracking().Where(r => r.TenantId == tenantId).ToListAsync(cancellationToken);
        var annualTypeByRuleSet = ruleSets.ToDictionary(
            r => r.Id,
            r => r.Types.FirstOrDefault(t => t.Code == LeaveTypeCode.Annual && t.HasBalance && !t.IsDisabled));
        var annualTypeIds = annualTypeByRuleSet.Values.Where(t => t is not null).Select(t => t!.Id).ToHashSet();

        var ledger = ids.Count == 0 || annualTypeIds.Count == 0
            ? []
            : await context.LedgerEntries.AsNoTracking()
                .Where(e => e.TenantId == tenantId && ids.Contains(e.EmployeeId) && annualTypeIds.Contains(e.LeaveTypeId))
                .ToListAsync(cancellationToken);
        var ledgerByEmployee = ledger.GroupBy(e => e.EmployeeId).ToDictionary(g => g.Key, g => (IReadOnlyCollection<LeaveLedgerEntry>)g.ToList());

        var columns = new List<ExportColumn>
        {
            new(localizer.Column("firstName")),
            new(localizer.Column("lastName")),
        };
        columns.AddRange(hierarchy.Levels.Select(l => new ExportColumn(l.Name)));
        columns.Add(new ExportColumn(localizer.Column("hireDate"), ExportColumnType.Date));
        columns.Add(new ExportColumn(localizer.Column("leaveAccrualStartDate"), ExportColumnType.Date));
        columns.Add(new ExportColumn(localizer.Column("status")));
        columns.Add(new ExportColumn(localizer.Column("remainingBalance"), ExportColumnType.Number));
        columns.Add(new ExportColumn(localizer.Column("totalEntitled"), ExportColumnType.Number));
        columns.Add(new ExportColumn(localizer.Column("carryOverLoss"), ExportColumnType.Number));
        columns.Add(new ExportColumn(localizer.Column("totalUsed"), ExportColumnType.Number));
        columns.Add(new ExportColumn(localizer.Column("futureApproved"), ExportColumnType.Number));
        columns.Add(new ExportColumn(localizer.Column("currentPeriod")));

        var data = new List<IReadOnlyList<object?>>(rows.Count);
        foreach (var row in rows)
        {
            var cells = new List<object?> { row.FirstName, row.LastName };
            cells.AddRange(hierarchy.LevelValues(row.OrgAssignments, row.OrgUnitId));

            var profile = profileByEmployee.GetValueOrDefault(row.EmployeeId);
            var annualType = profile is not null ? annualTypeByRuleSet.GetValueOrDefault(profile.RuleSetId) : null;
            cells.Add(profile?.HireDate ?? row.HireDate);
            cells.Add(profile?.LeaveAccrualStartDate);
            cells.Add(localizer.EnumText("EmployeeStatus", row.Status));

            if (profile is null || annualType is null)
            {
                cells.AddRange([null, null, null, null, null, null]);
            }
            else
            {
                var entries = ledgerByEmployee.GetValueOrDefault(row.EmployeeId) ?? [];
                var balance = LeaveBalanceEngine.Project(annualType, profile.LeaveAccrualStartDate, today, entries);
                var extraGrants = entries.Where(e => e.Type == LeaveLedgerEntryType.ExtraGrant && e.EffectiveDate <= today).Sum(e => e.Amount);
                var period = new LeavePeriod(balance.CurrentPeriodFrom, balance.CurrentPeriodTo);

                cells.Add(balance.AvailableBalance);
                cells.Add(balance.TotalAccrued + extraGrants);
                cells.Add(LeaveBalanceEngine.CarryOverLoss(period, entries));
                cells.Add(balance.TotalUsed);
                cells.Add(balance.FutureApprovedInPeriod);
                cells.Add($"{balance.CurrentPeriodFrom:dd.MM.yyyy} – {balance.CurrentPeriodTo:dd.MM.yyyy}");
            }

            data.Add(cells);
        }

        var sheet = localizer.Sheet("leaveBalanceSummary");
        var file = exporter.ExportToExcel($"{sheet} {today:yyyy-MM-dd}.xlsx", [new ExportTable(sheet, columns, data)], cancellationToken);
        return new ReportFileResult(file.FileName, file.ContentType, file.Content);
    }
}

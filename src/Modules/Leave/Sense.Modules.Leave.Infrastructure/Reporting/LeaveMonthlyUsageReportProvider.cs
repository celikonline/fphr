using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Leave.Infrastructure.Persistence;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.Export;

namespace Sense.Modules.Leave.Infrastructure.Reporting;

/// <summary>
/// Kolay İK "Hazır Şablonlar" kartı "Bu ayın kullanılan izin raporu" (Raporlar → İdari): <see cref="LeaveRequestsReportProvider"/>
/// ile aynı veri kaynağını kullanır ama iki farkla - (1) her durumu değil yalnız onaylı (kullanılmış) talepleri listeler
/// (kart senseik-030 §1'deki "kullanım" tanımıyla tutarlı, bkz. <see cref="LeaveReconciliationReportProvider"/>), (2)
/// serbest tarih aralığı yerine Kolay İK'nin Ay/Yıl hazır şablon semantiğini kullanır (ikisi de boşsa cari ay). Kolay
/// İK'de İdari menüsünde yer aldığı için kategorisi Administrative'dir, <see cref="LeaveRequestsReportProvider"/>'ın
/// (İzinler, serbest aralık, her durum) yerine değil yanına eklenir. Filtreler: Ay, Yıl, Birimler, işten ayrılanları
/// dahil et. Satır kümesi çağıranın leave.requests.read kapsamıdır.
/// </summary>
public sealed class LeaveMonthlyUsageReportProvider(
    LeaveDbContext context,
    IDataScopeService dataScope,
    IOrgUnitLookup orgUnits,
    IEmployeeReportReader employees,
    IReportExporter exporter,
    IStringLocalizer<SharedResource> localizer,
    TimeProvider clock) : IReportProvider
{
    public const string ReportKey = "leave.monthly_usage";

    public string Key => ReportKey;

    public string CategoryKey => ReportCategories.Administrative;

    public string? RequiredPermission => LeavePermissions.RequestsRead.Key;

    public IReadOnlyList<ReportParameterDescriptor> Parameters { get; } =
    [
        new(ReportParameterKeys.Year, ReportParameterKind.Year),
        new(ReportParameterKeys.Month, ReportParameterKind.Month),
        new(ReportParameterKeys.OrgUnitIds, ReportParameterKind.OrgUnits),
        new(ReportParameterKeys.IncludeTerminated, ReportParameterKind.Boolean),
    ];

    public async Task<ReportFileResult> GenerateAsync(Guid tenantId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var defaultMonthStart = new DateOnly(today.Year, today.Month, 1);
        var (from, to) = ReportParameters.ResolveMonthYearRange(parameters, today) ?? (defaultMonthStart, defaultMonthStart.AddMonths(1).AddDays(-1));

        var (rows, hierarchy) = await new ReportEmployeeSet(dataScope, orgUnits, employees)
            .LoadAsync(tenantId, LeavePermissions.RequestsRead.Key, parameters, cancellationToken);
        var rowByEmployee = rows.ToDictionary(r => r.EmployeeId);
        var ids = rows.Select(r => r.EmployeeId).ToList();

        var requests = ids.Count == 0
            ? []
            : await context.Requests.AsNoTracking()
                .Where(r => r.TenantId == tenantId && ids.Contains(r.EmployeeId) && r.Status == LeaveRequestStatus.Approved
                    && r.StartDate <= to && r.EndDate >= from)
                .OrderBy(r => r.StartDate).ThenBy(r => r.SubmittedAt)
                .ToListAsync(cancellationToken);

        var typeNames = await context.LeaveTypes.AsNoTracking()
            .Where(t => t.TenantId == tenantId)
            .Select(t => new { t.Id, t.Name })
            .ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);

        var columns = new List<ExportColumn>
        {
            new(localizer.Column("firstName")),
            new(localizer.Column("lastName")),
        };
        columns.AddRange(hierarchy.Levels.Select(l => new ExportColumn(l.Name)));
        columns.Add(new ExportColumn(localizer.Column("leaveType")));
        columns.Add(new ExportColumn(localizer.Column("startDate"), ExportColumnType.Date));
        columns.Add(new ExportColumn(localizer.Column("endDate"), ExportColumnType.Date));
        columns.Add(new ExportColumn(localizer.Column("days"), ExportColumnType.Number));
        columns.Add(new ExportColumn(localizer.Column("description")));

        var data = new List<IReadOnlyList<object?>>(requests.Count);
        foreach (var request in requests)
        {
            var employee = rowByEmployee[request.EmployeeId];
            var cells = new List<object?> { employee.FirstName, employee.LastName };
            cells.AddRange(hierarchy.LevelValues(employee.OrgAssignments, employee.OrgUnitId));
            cells.Add(typeNames.GetValueOrDefault(request.LeaveTypeId) ?? localizer.EnumText<LeaveTypeCode>(request.LeaveTypeCodeSnapshot));
            cells.Add(request.StartDate);
            cells.Add(request.EndDate);
            cells.Add(request.CalculatedDays);
            cells.Add(request.Description);
            data.Add(cells);
        }

        var sheet = localizer.Sheet("leaveMonthlyUsage");
        var file = exporter.ExportToExcel($"{sheet} {from:yyyy-MM-dd} {to:yyyy-MM-dd}.xlsx", [new ExportTable(sheet, columns, data)], cancellationToken);
        return new ReportFileResult(file.FileName, file.ContentType, file.Content);
    }
}

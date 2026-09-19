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
/// "İzin mutabakat formu" (kart senseik-030): bir dönemde çalışanın fiilen kullandığı (onaylanmış) izinlerin özeti,
/// satırın sonundaki boş "Çalışan imzası"/"İK onayı" sütunlarıyla basılıp elle imzalanabilir tek sayfalık liste (Kolay
/// İK'daki ayrı belge-başına-form yerine departman/birim bazlı toplu liste - <see cref="IReportExporter"/> yalnız
/// düz tablo üretebiliyor, PDF render'ı henüz yok - <c>IDocumentRenderer</c> sözleşmesi var ama implementasyonu ayrı
/// bir tur; handoff bunu not eder). Yalnız Approved talepler "kullanım" sayılır (Pending/Rejected/Cancelled hariç).
/// Filtreler: Tarih aralığı (zorunlu, "dönem"), Birimler, işten ayrılanları dahil et. Satır kümesi çağıranın
/// leave.requests.read kapsamıdır (LeaveRequestsReportProvider ile aynı yetki - form, izin taleplerinin bir özeti).
/// </summary>
public sealed class LeaveReconciliationReportProvider(
    LeaveDbContext context,
    IDataScopeService dataScope,
    IOrgUnitLookup orgUnits,
    IEmployeeReportReader employees,
    IReportExporter exporter,
    IStringLocalizer<SharedResource> localizer) : IReportProvider
{
    public const string ReportKey = "leave.reconciliation.form";

    public string Key => ReportKey;

    public string CategoryKey => ReportCategories.Leave;

    public string? RequiredPermission => LeavePermissions.RequestsRead.Key;

    public IReadOnlyList<ReportParameterDescriptor> Parameters { get; } =
    [
        new(ReportParameterKeys.DateRange, ReportParameterKind.DateRange, IsRequired: true),
        new(ReportParameterKeys.OrgUnitIds, ReportParameterKind.OrgUnits),
        new(ReportParameterKeys.IncludeTerminated, ReportParameterKind.Boolean),
    ];

    public async Task<ReportFileResult> GenerateAsync(Guid tenantId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        // Validated upstream (ReportParameterValidation) - a missing range here means a caller bypassed the command pipeline.
        var (from, to) = ReportParameters.GetDateRange(parameters)
            ?? throw new InvalidOperationException("Leave reconciliation report requires a from/to date range.");

        var (rows, hierarchy) = await new ReportEmployeeSet(dataScope, orgUnits, employees)
            .LoadAsync(tenantId, LeavePermissions.RequestsRead.Key, parameters, cancellationToken);
        var ids = rows.Select(r => r.EmployeeId).ToList();

        var requests = ids.Count == 0
            ? []
            : await context.Requests.AsNoTracking()
                .Where(r => r.TenantId == tenantId && ids.Contains(r.EmployeeId) && r.Status == LeaveRequestStatus.Approved
                    && r.StartDate <= to && r.EndDate >= from)
                .ToListAsync(cancellationToken);
        var requestsByEmployee = requests.GroupBy(r => r.EmployeeId).ToDictionary(g => g.Key, g => (IReadOnlyList<LeaveRequest>)g.ToList());

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
        columns.Add(new ExportColumn(localizer.Column("reconciliationPeriod")));
        columns.Add(new ExportColumn(localizer.Column("reconciliationUsageDetail")));
        columns.Add(new ExportColumn(localizer.Column("totalUsed"), ExportColumnType.Number));
        columns.Add(new ExportColumn(localizer.Column("reconciliationEmployeeSignature")));
        columns.Add(new ExportColumn(localizer.Column("reconciliationEmployeeSignatureDate")));
        columns.Add(new ExportColumn(localizer.Column("reconciliationHrApproval")));
        columns.Add(new ExportColumn(localizer.Column("reconciliationHrApprovalDate")));

        var period = $"{from:dd.MM.yyyy} – {to:dd.MM.yyyy}";
        var data = new List<IReadOnlyList<object?>>(rows.Count);
        foreach (var row in rows)
        {
            var cells = new List<object?> { row.FirstName, row.LastName };
            cells.AddRange(hierarchy.LevelValues(row.OrgAssignments, row.OrgUnitId));
            cells.Add(period);

            var employeeRequests = requestsByEmployee.GetValueOrDefault(row.EmployeeId) ?? [];
            var byType = employeeRequests
                .GroupBy(r => r.LeaveTypeId)
                .Select(g => (Name: typeNames.GetValueOrDefault(g.Key) ?? g.Key.ToString(), Days: g.Sum(r => r.CalculatedDays)))
                .OrderByDescending(t => t.Days)
                .ToList();
            cells.Add(byType.Count == 0 ? localizer.Column("reconciliationNoUsage") : string.Join("; ", byType.Select(t => $"{t.Name}: {t.Days.ToString("0.####")}")));
            cells.Add(byType.Sum(t => t.Days));

            // Signature/approval fields are intentionally blank - printed and hand-signed.
            cells.AddRange([null, null, null, null]);
            data.Add(cells);
        }

        var sheet = localizer.Sheet("leaveReconciliation");
        var file = exporter.ExportToExcel($"{sheet} {from:yyyy-MM-dd} {to:yyyy-MM-dd}.xlsx", [new ExportTable(sheet, columns, data)], cancellationToken);
        return new ReportFileResult(file.FileName, file.ContentType, file.Content);
    }
}

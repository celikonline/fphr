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
/// Kolay İK "İzin raporu" (Raporlar → İzinler; part8 #27, 11.3.12 `leave.requests`): tarih aralığıyla kesişen izin
/// talepleri - Ad, Soyad, kariyer kolonları, İzin türü, Başlangıç, Bitiş, Gün, Durum, Açıklama, Talep/Karar tarihi,
/// Kaynak. Her durum listelenir (onaylı, bekleyen, reddedilen, iptal) - Durum kolonu ayırır. Filtreler: Tarih aralığı
/// (zorunlu), Birimler, işten ayrılanları dahil et. Satır kümesi çağıranın leave.requests.read kapsamıdır. "İmzalı"
/// kolonu (roadmap #25) izin formu imza bayrağı gelince eklenecektir.
/// </summary>
public sealed class LeaveRequestsReportProvider(
    LeaveDbContext context,
    IDataScopeService dataScope,
    IOrgUnitLookup orgUnits,
    IEmployeeReportReader employees,
    IReportExporter exporter,
    IStringLocalizer<SharedResource> localizer) : IReportProvider
{
    public const string ReportKey = "leave.requests";

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
            ?? throw new InvalidOperationException("Leave report requires a from/to date range.");

        var (rows, hierarchy) = await new ReportEmployeeSet(dataScope, orgUnits, employees)
            .LoadAsync(tenantId, LeavePermissions.RequestsRead.Key, parameters, cancellationToken);
        var rowByEmployee = rows.ToDictionary(r => r.EmployeeId);
        var ids = rows.Select(r => r.EmployeeId).ToList();

        var requests = ids.Count == 0
            ? []
            : await context.Requests.AsNoTracking()
                .Where(r => r.TenantId == tenantId && ids.Contains(r.EmployeeId) && r.StartDate <= to && r.EndDate >= from)
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
        columns.Add(new ExportColumn(localizer.Column("requestStatus")));
        columns.Add(new ExportColumn(localizer.Column("description")));
        columns.Add(new ExportColumn(localizer.Column("submittedAt"), ExportColumnType.DateTime));
        columns.Add(new ExportColumn(localizer.Column("decidedAt"), ExportColumnType.DateTime));
        columns.Add(new ExportColumn(localizer.Column("source")));

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
            cells.Add(localizer.EnumText<LeaveRequestStatus>(request.Status));
            cells.Add(request.Description);
            cells.Add(request.SubmittedAt);
            cells.Add(request.DecidedAt ?? request.CancelledAt);
            cells.Add(localizer.EnumText<LeaveRequestSource>(request.Source));
            data.Add(cells);
        }

        var sheet = localizer.Sheet("leaveRequests");
        var file = exporter.ExportToExcel($"{sheet} {from:yyyy-MM-dd} {to:yyyy-MM-dd}.xlsx", [new ExportTable(sheet, columns, data)], cancellationToken);
        return new ReportFileResult(file.FileName, file.ContentType, file.Content);
    }
}

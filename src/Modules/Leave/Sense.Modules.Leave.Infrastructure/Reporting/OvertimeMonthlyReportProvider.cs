using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Overtime;
using Sense.Modules.Leave.Infrastructure.Persistence;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.Export;

namespace Sense.Modules.Leave.Infrastructure.Reporting;

/// <summary>
/// "Aylık fazla mesai raporu" (kart senseik-030): bir aydaki onaylanmış fazla mesai taleplerinin dökümü - süre, ücret
/// oranı (kart senseik-012 §1, %25/%50) ve varsa dönüşüm (ek izne mi yoksa ödemeye mi çevrildi - kart senseik-030 §3).
/// Dönem "Ay"/"Yıl" filtreleriyle seçilir (Kolay İK semantiği, <see cref="ReportParameters.ResolveMonthYearRange"/>);
/// ikisi de boşsa cari ay varsayılır. Yalnız Approved talepler listelenir (bekleyen/reddedilen bordroya girmez).
/// Satır kümesi çağıranın leave.overtimes.read kapsamıdır.
/// </summary>
public sealed class OvertimeMonthlyReportProvider(
    LeaveDbContext context,
    IDataScopeService dataScope,
    IOrgUnitLookup orgUnits,
    IEmployeeReportReader employees,
    IReportExporter exporter,
    IStringLocalizer<SharedResource> localizer,
    TimeProvider clock) : IReportProvider
{
    public const string ReportKey = "leave.overtime.monthly";

    public string Key => ReportKey;

    public string CategoryKey => ReportCategories.Leave;

    public string? RequiredPermission => LeavePermissions.OvertimesRead.Key;

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
        var (from, to) = ReportParameters.ResolveMonthYearRange(parameters, today)
            ?? (new DateOnly(today.Year, today.Month, 1), new DateOnly(today.Year, today.Month, 1).AddMonths(1).AddDays(-1));

        var (rows, hierarchy) = await new ReportEmployeeSet(dataScope, orgUnits, employees)
            .LoadAsync(tenantId, LeavePermissions.OvertimesRead.Key, parameters, cancellationToken);
        var rowByEmployee = rows.ToDictionary(r => r.EmployeeId);
        var ids = rows.Select(r => r.EmployeeId).ToList();

        var requests = ids.Count == 0
            ? []
            : await context.OvertimeRequests.AsNoTracking()
                .Where(r => r.TenantId == tenantId && ids.Contains(r.EmployeeId) && r.Status == OvertimeRequestStatus.Approved
                    && r.WorkDate >= from && r.WorkDate <= to)
                .OrderBy(r => r.WorkDate).ThenBy(r => r.StartTime)
                .ToListAsync(cancellationToken);

        var conversionIds = requests.Where(r => r.ConversionId is not null).Select(r => r.ConversionId!.Value).ToList();
        var conversions = conversionIds.Count == 0
            ? new Dictionary<Guid, OvertimeConversion>()
            : await context.OvertimeConversions.AsNoTracking().Where(c => conversionIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);

        var columns = new List<ExportColumn>
        {
            new(localizer.Column("firstName")),
            new(localizer.Column("lastName")),
        };
        columns.AddRange(hierarchy.Levels.Select(l => new ExportColumn(l.Name)));
        columns.Add(new ExportColumn(localizer.Column("workDate"), ExportColumnType.Date));
        columns.Add(new ExportColumn(localizer.Column("startTime")));
        columns.Add(new ExportColumn(localizer.Column("durationMinutes"), ExportColumnType.Number));
        columns.Add(new ExportColumn(localizer.Column("payableMinutes"), ExportColumnType.Number));
        columns.Add(new ExportColumn(localizer.Column("payRateKind")));
        columns.Add(new ExportColumn(localizer.Column("conversionKind")));
        columns.Add(new ExportColumn(localizer.Column("conversionStatus")));
        columns.Add(new ExportColumn(localizer.Column("description")));

        var data = new List<IReadOnlyList<object?>>(requests.Count);
        foreach (var request in requests)
        {
            var employee = rowByEmployee[request.EmployeeId];
            var cells = new List<object?> { employee.FirstName, employee.LastName };
            cells.AddRange(hierarchy.LevelValues(employee.OrgAssignments, employee.OrgUnitId));
            cells.Add(request.WorkDate);
            cells.Add(request.StartTime.ToString("HH:mm"));
            cells.Add(request.DurationMinutes);
            cells.Add(request.PayableMinutes);
            cells.Add(localizer.EnumText<OvertimePayRateKind>(request.PayRateKind));

            var conversion = request.ConversionId is { } conversionId ? conversions.GetValueOrDefault(conversionId) : null;
            cells.Add(conversion is null ? null : localizer.EnumText<OvertimeConversionKind>(conversion.Kind));
            cells.Add(conversion is null ? null : localizer.EnumText<OvertimeConversionStatus>(conversion.Status));
            cells.Add(request.Description);
            data.Add(cells);
        }

        var sheet = localizer.Sheet("overtimeMonthly");
        var file = exporter.ExportToExcel($"{sheet} {from:yyyy-MM-dd} {to:yyyy-MM-dd}.xlsx", [new ExportTable(sheet, columns, data)], cancellationToken);
        return new ReportFileResult(file.FileName, file.ContentType, file.Content);
    }
}

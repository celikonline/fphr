using Microsoft.Extensions.Localization;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.Export;

namespace Sense.Modules.Attendance.Infrastructure.Reporting;

/// <summary>
/// Aylık puantaj özeti (senseik-029, kolayik.com/puantaj rakip analizi, "Raporlar > Çalışan" - 11.3.12 satır 252 "Çalışma
/// takvimi raporu, kolonlar doğrulanmalı"): Ad, Soyad, kariyer kolonları, kayıtlı/çalışılan gün sayısı, çalışılan/fazla
/// mesai/gece saatleri ve çip sayaçları (geç kalma, erken çıkış, eksik gün, izinli günde çalışma, çözülmemiş). Yalnız
/// zaten var olan AttendanceDay kayıtlarını okur - ne bir hesaplama ne bir yazma yapar (bkz. AttendanceMonthlyAggregate).
/// Filtreler: Yıl+Ay (zorunlu), Birimler, işten ayrılanları dahil et. Satır kümesi çağıranın attendance.timesheet.view
/// kapsamıdır.
/// </summary>
public sealed class AttendanceMonthlySummaryReportProvider(
    IAttendanceDayRepository days,
    IDataScopeService dataScope,
    IOrgUnitLookup orgUnits,
    IEmployeeReportReader employees,
    IReportExporter exporter,
    ISimplePdfWriter pdfWriter,
    IStringLocalizer<SharedResource> localizer) : IReportProvider
{
    public const string ReportKey = "attendance.monthly.summary";

    public string Key => ReportKey;

    public string CategoryKey => ReportCategories.Employee;

    public string? RequiredPermission => "attendance.timesheet.view";

    public IReadOnlyList<ReportParameterDescriptor> Parameters { get; } =
    [
        new(ReportParameterKeys.Year, ReportParameterKind.Year, IsRequired: true),
        new(ReportParameterKeys.Month, ReportParameterKind.Month, IsRequired: true),
        new(ReportParameterKeys.OrgUnitIds, ReportParameterKind.OrgUnits),
        new(ReportParameterKeys.IncludeTerminated, ReportParameterKind.Boolean),
    ];

    public async Task<ReportFileResult> GenerateAsync(Guid tenantId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var (from, to) = AttendanceReportPeriod.Resolve(parameters);

        var (rows, hierarchy) = await new ReportEmployeeSet(dataScope, orgUnits, employees)
            .LoadAsync(tenantId, "attendance.timesheet.view", parameters, cancellationToken);
        var ids = rows.Select(r => r.EmployeeId).ToList();

        var records = ids.Count == 0 ? [] : await days.ListForEmployeesAsync(ids, from, to, cancellationToken);
        var recordsByEmployee = records.ToLookup(d => d.EmployeeId);

        var columns = new List<ExportColumn>
        {
            new(localizer.Column("firstName")),
            new(localizer.Column("lastName")),
        };
        columns.AddRange(hierarchy.Levels.Select(l => new ExportColumn(l.Name)));
        columns.Add(new ExportColumn(localizer.Column("recordedDays"), ExportColumnType.Number));
        columns.Add(new ExportColumn(localizer.Column("workedDays"), ExportColumnType.Number));
        columns.Add(new ExportColumn(localizer.Column("workedHours"), ExportColumnType.Number));
        columns.Add(new ExportColumn(localizer.Column("overtimeHours"), ExportColumnType.Number));
        columns.Add(new ExportColumn(localizer.Column("nightHours"), ExportColumnType.Number));
        columns.Add(new ExportColumn(localizer.Column("lateArrivalCount"), ExportColumnType.Number));
        columns.Add(new ExportColumn(localizer.Column("earlyLeaveCount"), ExportColumnType.Number));
        columns.Add(new ExportColumn(localizer.Column("missingDayCount"), ExportColumnType.Number));
        columns.Add(new ExportColumn(localizer.Column("workedWhileOnLeaveCount"), ExportColumnType.Number));
        columns.Add(new ExportColumn(localizer.Column("unresolvedAnomalyCount"), ExportColumnType.Number));

        var data = new List<IReadOnlyList<object?>>(rows.Count);
        foreach (var row in rows)
        {
            var aggregate = AttendanceMonthlyAggregate.From(recordsByEmployee[row.EmployeeId].ToList());
            var cells = new List<object?> { row.FirstName, row.LastName };
            cells.AddRange(hierarchy.LevelValues(row.OrgAssignments, row.OrgUnitId));
            cells.Add(aggregate.RecordedDays);
            cells.Add(aggregate.WorkedDays);
            cells.Add(aggregate.WorkedHours);
            cells.Add(aggregate.OvertimeHours);
            cells.Add(aggregate.NightHours);
            cells.Add(aggregate.LateArrivalCount);
            cells.Add(aggregate.EarlyLeaveCount);
            cells.Add(aggregate.MissingDayCount);
            cells.Add(aggregate.WorkedWhileOnLeaveCount);
            cells.Add(aggregate.UnresolvedCount);
            data.Add(cells);
        }

        var sheet = localizer.Sheet("attendanceMonthlySummary");
        var fileNameStem = $"{sheet} {from:yyyy-MM}";

        if (AttendanceReportPdfExport.IsPdfRequested(parameters))
        {
            var document = AttendanceReportPdfExport.BuildDocument(sheet, columns, data, from, to);
            return AttendanceReportPdfExport.Write(pdfWriter, document, fileNameStem);
        }

        var file = exporter.ExportToExcel($"{fileNameStem}.xlsx", [new ExportTable(sheet, columns, data)], cancellationToken);
        return new ReportFileResult(file.FileName, file.ContentType, file.Content);
    }
}

/// <summary>Yıl+Ay parametresinden [ayın ilk günü, ayın son günü] aralığı - her iki puantaj rapor sağlayıcısı da aynı kuralı paylaşır.</summary>
internal static class AttendanceReportPeriod
{
    public static (DateOnly From, DateOnly To) Resolve(IReadOnlyDictionary<string, string> parameters)
    {
        // Validated upstream (ReportParameterValidation) - eksik değer, çağıranın komut hattını atladığı anlamına gelir.
        var year = ReportParameters.GetInt(parameters, ReportParameterKeys.Year)
            ?? throw new InvalidOperationException("Attendance monthly report requires a year parameter.");
        var month = ReportParameters.GetInt(parameters, ReportParameterKeys.Month)
            ?? throw new InvalidOperationException("Attendance monthly report requires a month parameter.");

        var from = new DateOnly(year, month, 1);
        return (from, from.AddMonths(1).AddDays(-1));
    }
}

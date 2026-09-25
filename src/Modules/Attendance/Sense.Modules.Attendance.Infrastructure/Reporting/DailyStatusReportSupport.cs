using Microsoft.Extensions.Localization;
using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.Export;

namespace Sense.Modules.Attendance.Infrastructure.Reporting;

/// <summary>
/// Günlük puantaj sorgusundan (<see cref="IDailyAttendanceQuery"/>) beslenen zaman raporlarının ortak gövdesi
/// (Hesaplanamayanlar, Günlük durum listeleri): tarih çözümü, sayfalı okuma, sütunlar ve çıktı. Veri kapsamı
/// (attendance.daily.view) sorgunun kendisinde uygulanır; burada tekrar hesaplanmaz.
/// </summary>
internal static class DailyStatusReportSupport
{
    /// <summary>Tek çalıştırmada en fazla bu kadar gün taranır (aralık verilirse başlangıçtan itibaren kırpılır).</summary>
    public const int MaxDays = 31;

    private const int PageSize = 200;
    private const string TimeFormat = "HH:mm";

    public static readonly IReadOnlyList<ReportParameterDescriptor> DateParameters =
    [
        new(ReportParameterKeys.DateRange, ReportParameterKind.DateRange),
        new(ReportParameterKeys.OrgUnitIds, ReportParameterKind.OrgUnits),
    ];

    /// <summary>"date" tek gün; yoksa from/to aralığı (en çok <see cref="MaxDays"/> gün); ikisi de yoksa kiracının bugünü.</summary>
    public static async Task<IReadOnlyList<DateOnly>> ResolveDaysAsync(
        ITenantLocalClock clock, Guid tenantId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        if (ReportParameters.GetDate(parameters, ReportParameterKeys.Date) is { } single)
        {
            return [single];
        }

        if (ReportParameters.GetDateRange(parameters) is { } range)
        {
            var count = Math.Min(range.To.DayNumber - range.From.DayNumber + 1, MaxDays);
            return Enumerable.Range(0, count).Select(range.From.AddDays).ToList();
        }

        return [DateOnly.FromDateTime((await clock.NowAsync(tenantId, cancellationToken)).Local)];
    }

    /// <summary>True when the requested from/to range is longer than <see cref="MaxDays"/> and <see cref="ResolveDaysAsync"/> cut it.</summary>
    public static bool IsRangeTruncated(IReadOnlyDictionary<string, string> parameters) =>
        ReportParameters.GetDate(parameters, ReportParameterKeys.Date) is null
        && ReportParameters.GetDateRange(parameters) is { } range
        && range.To.DayNumber - range.From.DayNumber + 1 > MaxDays;

    public static async Task<IReadOnlyList<DailyAttendanceRowDto>> LoadAsync(
        IDailyAttendanceQuery query, Guid tenantId, IReadOnlyList<DateOnly> days, DailyStatus status,
        IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var units = ReportParameters.GetGuidList(parameters, ReportParameterKeys.OrgUnitIds);
        Guid?[] unitFilters = units.Count == 0 ? [null] : units.Select(u => (Guid?)u).ToArray();

        var rows = new List<DailyAttendanceRowDto>();
        foreach (var day in days)
        {
            var seen = new HashSet<Guid>();
            foreach (var unit in unitFilters)
            {
                foreach (var row in await ReadAllPagesAsync(query, tenantId, day, status, unit, cancellationToken))
                {
                    if (seen.Add(row.EmployeeId))
                    {
                        rows.Add(row);
                    }
                }
            }
        }

        return rows;
    }

    public static ReportFileResult Produce(
        IReadOnlyDictionary<string, string> parameters, IReportExporter exporter, ISimplePdfWriter pdfWriter, IStringLocalizer localizer,
        string sheetKey, IReadOnlyList<DateOnly> days, IReadOnlyList<DailyAttendanceRowDto> rows, CancellationToken cancellationToken)
    {
        ExportColumn[] columns =
        [
            new(localizer.Column("employeeName")),
            new(localizer.Column("date"), ExportColumnType.Date),
            new(localizer.Column("checkIn")),
            new(localizer.Column("checkOut")),
            new(localizer.Column("workedMinutes"), ExportColumnType.Number),
            new(localizer.Column("overtimeMinutes"), ExportColumnType.Number),
            new(localizer.Column("anomalies")),
            new(localizer.Column("leaveType")),
        ];
        var data = rows
            .Select(r => (IReadOnlyList<object?>)new List<object?>
            {
                r.EmployeeName, r.Date, r.CheckIn?.ToString(TimeFormat), r.CheckOut?.ToString(TimeFormat),
                r.WorkedMinutes, r.OvertimeMinutes, string.Join(", ", r.Anomalies.Select(a => localizer["report.anomaly." + a].Value)), r.LeaveType,
            })
            .ToList();

        var sheet = localizer.Sheet(sheetKey);
        var first = days[0];
        var last = days[^1];
        var period = AttendanceReportPdfExport.FormatPeriod(first, last);
        if (IsRangeTruncated(parameters))
        {
            // The requested range was cut to MaxDays: say so in the PDF period line and as a closing Excel row.
            var note = localizer["report.note.daily_range_truncated", MaxDays].Value;
            period = $"{period} ({note})";
            if (!AttendanceReportPdfExport.IsPdfRequested(parameters))
            {
                data.Add([note, .. new object?[columns.Length - 1]]);
            }
        }

        return AttendanceReportPdfExport.Produce(
            parameters, exporter, pdfWriter, sheet, $"{sheet} {first:yyyy-MM-dd}", columns, data, period, cancellationToken);
    }

    private static async Task<IReadOnlyList<DailyAttendanceRowDto>> ReadAllPagesAsync(
        IDailyAttendanceQuery query, Guid tenantId, DateOnly day, DailyStatus status, Guid? unit, CancellationToken cancellationToken)
    {
        var all = new List<DailyAttendanceRowDto>();
        for (var page = 1; ; page++)
        {
            var result = await query.ListAsync(tenantId, day, status, unit, true, page, PageSize, cancellationToken);
            all.AddRange(result.Items);
            if (result.Items.Count == 0 || all.Count >= result.TotalCount)
            {
                return all;
            }
        }
    }
}

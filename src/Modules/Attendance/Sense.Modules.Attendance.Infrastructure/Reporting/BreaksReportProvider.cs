using Microsoft.Extensions.Localization;
using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.Export;

namespace Sense.Modules.Attendance.Infrastructure.Reporting;

/// <summary>
/// Mola (Z4.1b): günün Out→In boşlukları mola sayılır (ilk giriş ile son çıkış arasındaki ara okutmalar; B1'in taşıdığı eski
/// günlerin işaretli "backfill-break:" çifti de aynı yolla sayılır). Satır: çalışan, gün, mola sayısı, toplam/en uzun mola dakikası,
/// planlı mola (plan çözümlemesinden) ve fark (toplam - planlı). Molası olmayan gün listelenmez.
/// Satır kümesi attendance.daily.view veri kapsamı ∩ birim süzgecidir; tarih verilmezse kiracının bugünü, aralık en çok 31 gündür.
/// </summary>
public sealed class BreaksReportProvider(
    Sense.Modules.Attendance.Domain.IPunchRepository punches,
    IPlannedDayResolver planner,
    ITenantLocalClock clock,
    ITenantTimeZoneLookup timeZones,
    IDataScopeService dataScope,
    IOrgUnitLookup orgUnits,
    IEmployeeReportReader employees,
    IReportExporter exporter,
    ISimplePdfWriter pdfWriter,
    IStringLocalizer<SharedResource> localizer) : IReportProvider
{
    public const string ReportKey = "attendance.time.breaks";

    public string Key => ReportKey;

    public string CategoryKey => ReportCategories.Time;

    public string? RequiredPermission => PunchReportSupport.ScopePermission;

    public IReadOnlyList<ReportParameterDescriptor> Parameters { get; } = PunchReportSupport.Parameters(dateRangeRequired: false);

    public async Task<ReportFileResult> GenerateAsync(Guid tenantId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var (from, to) = await PunchReportSupport.ResolveRangeAsync(clock, tenantId, parameters, cancellationToken);
        var people = await PunchReportSupport.LoadPeopleAsync(dataScope, orgUnits, employees, tenantId, parameters, cancellationToken);
        var zone = await PunchReportSupport.ZoneAsync(timeZones, tenantId, cancellationToken);
        var all = people.Count == 0 ? [] : await punches.ListForRangeAsync(tenantId, from, to, people.Keys.ToList(), cancellationToken);

        ExportColumn[] columns =
        [
            new(localizer.Column("employeeName")),
            new(localizer.Column("workDate"), ExportColumnType.Date),
            new(localizer.Column("breakCount"), ExportColumnType.Number),
            new(localizer.Column("totalBreakMinutes"), ExportColumnType.Number),
            new(localizer.Column("longestBreakMinutes"), ExportColumnType.Number),
            new(localizer.Column("plannedBreakMinutes"), ExportColumnType.Number),
            new(localizer.Column("breakDifferenceMinutes"), ExportColumnType.Number),
        ];
        var data = new List<(string Name, DateOnly Day, IReadOnlyList<object?> Cells)>();
        foreach (var group in all.Where(p => !p.IsDeleted).GroupBy(p => (p.EmployeeId, p.WorkDate)))
        {
            var ordered = group.OrderBy(p => p.OccurredAtUtc).ToList();
            var gaps = Gaps(ordered);
            if (gaps.Count == 0)
            {
                continue;
            }

            var total = gaps.Sum();
            var firstIn = ordered.FirstOrDefault(p => p.Direction == PunchDirection.In);
            var planned = await planner.ResolveAsync(
                group.Key.EmployeeId, group.Key.WorkDate,
                firstIn is null ? null : TimeOnly.FromDateTime(PunchReportSupport.ToLocal(firstIn.OccurredAtUtc, zone)), cancellationToken);
            var plannedBreak = planned?.BreakMinutes ?? 0; // plan yok (I0: PlannedDay?) → planlı mola 0
            var name = PunchReportSupport.Name(people, group.Key.EmployeeId);
            data.Add((name, group.Key.WorkDate, new List<object?>
            {
                name, group.Key.WorkDate, gaps.Count, total, gaps.Max(), plannedBreak, total - plannedBreak,
            }));
        }

        var rows = data.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(d => d.Day).Select(d => d.Cells).ToList();
        return PunchReportSupport.Produce(
            parameters, exporter, pdfWriter, localizer, "attendanceBreaks", AttendanceReportPdfExport.FormatPeriod(from, to), from, columns, rows, cancellationToken);
    }

    /// <summary>Çıkış okutmasını hemen izleyen giriş okutması arasındaki dakikalar (zaman sırasındaki Out→In çiftleri).</summary>
    private static List<int> Gaps(IReadOnlyList<Sense.Modules.Attendance.Domain.Punches.Punch> ordered)
    {
        var gaps = new List<int>();
        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i - 1].Direction == PunchDirection.Out && ordered[i].Direction == PunchDirection.In)
            {
                gaps.Add((int)Math.Round((ordered[i].OccurredAtUtc - ordered[i - 1].OccurredAtUtc).TotalMinutes));
            }
        }

        return gaps;
    }
}

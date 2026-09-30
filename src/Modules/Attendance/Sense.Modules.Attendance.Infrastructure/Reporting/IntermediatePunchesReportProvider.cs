using Sense.Modules.Attendance.Application.Punches;
using Microsoft.Extensions.Localization;
using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Modules.Attendance.Domain;
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
/// Ara Kayıtlar (Z4.1b): bir çalışanın günündeki ilk okutma (giriş) ile son okutma (çıkış) DIŞINDaki okutmalar (ara Out/In).
/// Günde en az üç okutma yoksa satır üretilmez. Sıra, günün zaman sırasındaki numarasıdır (ilk okutma 1). Silinmiş okutmalar dahil değildir.
/// Satır kümesi attendance.daily.view veri kapsamı ∩ birim süzgecidir; tarih verilmezse kiracının bugünü, aralık en çok 31 gündür.
/// </summary>
public sealed class IntermediatePunchesReportProvider(
    Sense.Modules.Attendance.Domain.IPunchRepository punches,
    ITerminalRepository terminals,
    ITenantLocalClock clock,
    ITenantTimeZoneLookup timeZones,
    IDataScopeService dataScope,
    IOrgUnitLookup orgUnits,
    IEmployeeReportReader employees,
    IReportExporter exporter,
    ISimplePdfWriter pdfWriter,
    IStringLocalizer<SharedResource> localizer) : IReportProvider
{
    public const string ReportKey = "attendance.time.intermediate_punches";

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
        var terminalNames = await PunchReportSupport.TerminalNamesAsync(terminals, cancellationToken);

        ExportColumn[] columns =
        [
            new(localizer.Column("employeeName")),
            new(localizer.Column("workDate"), ExportColumnType.Date),
            new(localizer.Column("sequence"), ExportColumnType.Number),
            new(localizer.Column("time")),
            new(localizer.Column("direction")),
            new(localizer.Column("source")),
            new(localizer.Column("terminal")),
        ];
        var data = new List<(string Name, DateOnly Day, int Sequence, IReadOnlyList<object?> Cells)>();
        foreach (var group in all.Where(p => !p.IsDeleted).GroupBy(p => (p.EmployeeId, p.WorkDate)))
        {
            var ordered = BackfillBreakFilter.WithoutSuperseded(group.OrderBy(p => p.OccurredAtUtc).ToList());
            var name = PunchReportSupport.Name(people, group.Key.EmployeeId);
            for (var i = 1; i < ordered.Count - 1; i++)
            {
                var p = ordered[i];
                data.Add((name, p.WorkDate, i + 1, new List<object?>
                {
                    name, p.WorkDate, i + 1, PunchReportSupport.ToLocal(p.OccurredAtUtc, zone).ToString(PunchReportSupport.TimeFormat),
                    PunchReportSupport.DirectionText(localizer, p.Direction), PunchReportSupport.SourceText(localizer, p.Source),
                    PunchReportSupport.TerminalName(terminalNames, p.TerminalId),
                }));
            }
        }

        var rows = data.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(d => d.Day).ThenBy(d => d.Sequence).Select(d => d.Cells).ToList();
        return PunchReportSupport.Produce(
            parameters, exporter, pdfWriter, localizer, "attendanceIntermediatePunches", AttendanceReportPdfExport.FormatPeriod(from, to), from, columns, rows, cancellationToken);
    }
}

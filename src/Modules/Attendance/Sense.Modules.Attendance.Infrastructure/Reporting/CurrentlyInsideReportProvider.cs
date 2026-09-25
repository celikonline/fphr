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
/// İçerdekiler (Z4.1b): kiracının yerel bugünü için anlık görüntü. Bugünkü (WorkDate) silinmemiş okutmalarında son okutması
/// <c>In</c> olan çalışanlar "içeride" sayılır; son okutması Out/yönsüz olan listelenmez. Tarih parametresi yoktur.
/// Satır kümesi attendance.daily.view veri kapsamı ∩ birim süzgecidir.
/// </summary>
public sealed class CurrentlyInsideReportProvider(
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
    public const string ReportKey = "attendance.time.currently_inside";

    public string Key => ReportKey;

    public string CategoryKey => ReportCategories.Time;

    public string? RequiredPermission => PunchReportSupport.ScopePermission;

    public IReadOnlyList<ReportParameterDescriptor> Parameters { get; } =
    [
        new(ReportParameterKeys.OrgUnitIds, ReportParameterKind.OrgUnits),
        new("format", ReportParameterKind.Select, Options: ["xlsx", "pdf"]),
    ];

    public async Task<ReportFileResult> GenerateAsync(Guid tenantId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime((await clock.NowAsync(tenantId, cancellationToken)).Local);
        var people = await PunchReportSupport.LoadPeopleAsync(dataScope, orgUnits, employees, tenantId, parameters, cancellationToken);
        var zone = await PunchReportSupport.ZoneAsync(timeZones, tenantId, cancellationToken);
        var todays = people.Count == 0 ? [] : await punches.ListForRangeAsync(tenantId, today, today, people.Keys.ToList(), cancellationToken);
        var terminalNames = await PunchReportSupport.TerminalNamesAsync(terminals, cancellationToken);
        var unitNames = (await orgUnits.ListActiveUnitsAsync(tenantId, cancellationToken)).ToDictionary(u => u.Id, u => u.Name);

        ExportColumn[] columns =
        [
            new(localizer.Column("employeeName")),
            new(localizer.Column("orgUnit")),
            new(localizer.Column("firstIn")),
            new(localizer.Column("lastPunch")),
            new(localizer.Column("source")),
            new(localizer.Column("terminal")),
        ];
        var data = new List<(string Name, IReadOnlyList<object?> Cells)>();
        foreach (var group in todays.Where(p => !p.IsDeleted).GroupBy(p => p.EmployeeId))
        {
            var ordered = group.OrderBy(p => p.OccurredAtUtc).ToList();
            var last = ordered[^1];
            if (last.Direction != PunchDirection.In || !people.TryGetValue(group.Key, out var person))
            {
                continue;
            }

            var firstIn = ordered.First(p => p.Direction == PunchDirection.In);
            var name = PunchReportSupport.Name(people, group.Key);
            data.Add((name, new List<object?>
            {
                name, person.OrgUnitId is { } unit ? unitNames.GetValueOrDefault(unit) : null,
                PunchReportSupport.ToLocal(firstIn.OccurredAtUtc, zone).ToString(PunchReportSupport.TimeFormat),
                PunchReportSupport.ToLocal(last.OccurredAtUtc, zone).ToString(PunchReportSupport.TimeFormat),
                PunchReportSupport.SourceText(localizer, last.Source), PunchReportSupport.TerminalName(terminalNames, last.TerminalId),
            }));
        }

        var rows = data.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).Select(d => d.Cells).ToList();
        return PunchReportSupport.Produce(
            parameters, exporter, pdfWriter, localizer, "attendanceCurrentlyInside", AttendanceReportPdfExport.FormatPeriod(today, today), today, columns, rows, cancellationToken);
    }
}

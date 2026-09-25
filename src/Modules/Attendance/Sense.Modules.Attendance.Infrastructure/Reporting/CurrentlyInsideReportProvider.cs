using Microsoft.Extensions.Localization;
using Sense.Modules.Attendance.Application.AttendanceDays;
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
    public const string ReportKey = "attendance.time.currently_inside";

    /// <summary>Dünden açık kalan giriş, plan gece vardiyası değilse bu süreden eskiyse "unutulmuş çıkış" sayılır ve listelenmez.</summary>
    public const int MaxOpenPresenceHours = 16;

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
        var now = await clock.NowAsync(tenantId, cancellationToken);
        var today = DateOnly.FromDateTime(now.Local);
        var yesterday = today.AddDays(-1);
        var people = await PunchReportSupport.LoadPeopleAsync(dataScope, orgUnits, employees, tenantId, parameters, cancellationToken);
        var zone = await PunchReportSupport.ZoneAsync(timeZones, tenantId, cancellationToken);
        var todays = people.Count == 0 ? [] : await punches.ListForRangeAsync(tenantId, yesterday, today, people.Keys.ToList(), cancellationToken);
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
            if (last.Direction != PunchDirection.In || !people.TryGetValue(group.Key, out var person)
                || !await IsStillInsideAsync(group.Key, last, today, now, zone, cancellationToken))
            {
                continue;
            }

            var firstIn = ordered.First(p => p.Direction == PunchDirection.In && p.WorkDate == last.WorkDate);
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

    /// <summary>Bugünün açık girişi içeridedir; dünün açık girişi yalnız gece vardiyası sürüyorsa ya da pencere içindeyse (unutulmuş çıkış değilse).</summary>
    private async Task<bool> IsStillInsideAsync(
        Guid employeeId, Sense.Modules.Attendance.Domain.Punches.Punch last, DateOnly today, TenantLocalNow now, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        if (last.WorkDate >= today)
        {
            return true;
        }

        if ((now.Utc - last.OccurredAtUtc).TotalHours <= MaxOpenPresenceHours)
        {
            return true;
        }

        var plan = await planner.ResolveAsync(employeeId, last.WorkDate, null, cancellationToken);
        return plan is { Start: { } start, End: { } end } && end <= start && TimeOnly.FromDateTime(now.Local) <= end;
    }
}

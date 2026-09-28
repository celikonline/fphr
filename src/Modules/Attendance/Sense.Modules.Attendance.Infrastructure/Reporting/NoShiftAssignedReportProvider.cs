using Microsoft.Extensions.Localization;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.WorkSchedules;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.Export;

namespace Sense.Modules.Attendance.Infrastructure.Reporting;

/// <summary>
/// Vardiya atanmayanlar (Z4.1): tarih aralığında çalışma tablosuna göre planlı çalışma günü olan ama o gün için hiç
/// ShiftAssignment satırı bulunmayan çalışanlar. Çalışma günü olmayan türde (hafta tatili/dinlenme/resmi tatil) bir
/// atama satırı da bir plan kararıdır; o gün "atanmamış" sayılmaz. İşe giriş öncesi ve işten çıkış sonrası günler sayılmaz.
/// Etkin çalışma tablosu: çalışanın tarihli ataması, yoksa varsayılan tablo. Satır kümesi attendance.daily.view kapsamıdır.
/// Parametreler: from/to (zorunlu, en çok 366 gün), birimler, işten ayrılanları dahil et, isteğe bağlı "format=pdf".
/// </summary>
public sealed class NoShiftAssignedReportProvider(
    IShiftAssignmentRepository shifts,
    IWorkScheduleRepository schedules,
    IEmployeeWorkScheduleAssignmentRepository scheduleAssignments,
    IDataScopeService dataScope,
    IOrgUnitLookup orgUnits,
    IEmployeeReportReader employees,
    IReportExporter exporter,
    ISimplePdfWriter pdfWriter,
    IStringLocalizer<SharedResource> localizer) : IReportProvider
{
    public const string ReportKey = "attendance.time.no_shift_assigned";
    private const int MaxRangeDays = 366;

    public string Key => ReportKey;

    public string CategoryKey => ReportCategories.Time;

    public string? RequiredPermission => "attendance.daily.view";

    public IReadOnlyList<ReportParameterDescriptor> Parameters { get; } =
    [
        new(ReportParameterKeys.DateRange, ReportParameterKind.DateRange, IsRequired: true),
        new(ReportParameterKeys.OrgUnitIds, ReportParameterKind.OrgUnits),
        new(ReportParameterKeys.IncludeTerminated, ReportParameterKind.Boolean),
    ];

    public async Task<ReportFileResult> GenerateAsync(Guid tenantId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var range = ReportParameters.GetDateRange(parameters)
            ?? throw new InvalidOperationException("No-shift-assigned report requires a valid from/to date range.");
        var from = range.From;
        var to = range.To.DayNumber - from.DayNumber >= MaxRangeDays ? from.AddDays(MaxRangeDays - 1) : range.To;

        var (people, hierarchy) = await new ReportEmployeeSet(dataScope, orgUnits, employees)
            .LoadAsync(tenantId, "attendance.daily.view", parameters, cancellationToken);

        var ids = people.Select(p => p.EmployeeId).ToList();
        var assigned = (ids.Count == 0 ? [] : await shifts.ListForRangeAsync(from, to, ids, cancellationToken))
            .ToLookup(a => a.EmployeeId, a => a.Date);
        var scheduleById = (await schedules.ListAsync(true, cancellationToken)).ToDictionary(s => s.Id);
        var defaultSchedule = await schedules.FindDefaultAsync(cancellationToken);

        var columns = new List<ExportColumn> { new(localizer.Column("firstName")), new(localizer.Column("lastName")) };
        columns.AddRange(hierarchy.Levels.Select(l => new ExportColumn(l.Name)));
        columns.Add(new ExportColumn(localizer.Column("unassignedDayCount"), ExportColumnType.Number));
        columns.Add(new ExportColumn(localizer.Column("firstUnassignedDate"), ExportColumnType.Date));

        var data = new List<IReadOnlyList<object?>>();
        foreach (var person in people)
        {
            var assignedDates = assigned[person.EmployeeId].ToHashSet();
            var windows = await ScheduleWindowsAsync(person.EmployeeId, from, to, scheduleById, cancellationToken);
            var missing = PlannedDays(person, from, to, windows, defaultSchedule).Where(d => !assignedDates.Contains(d)).ToList();
            if (missing.Count == 0)
            {
                continue;
            }

            var cells = new List<object?> { person.FirstName, person.LastName };
            cells.AddRange(hierarchy.LevelValues(person.OrgAssignments, person.OrgUnitId));
            cells.Add(missing.Count);
            cells.Add(missing[0]);
            data.Add(cells);
        }

        var sheet = localizer.Sheet("attendanceNoShiftAssigned");
        return AttendanceReportPdfExport.Produce(
            parameters, exporter, pdfWriter, sheet, $"{sheet} {from:yyyy-MM-dd}", columns, data,
            AttendanceReportPdfExport.FormatPeriod(from, to), cancellationToken);
    }

    private async Task<IReadOnlyList<(EmployeeWorkScheduleAssignment Assignment, WorkSchedule Schedule)>> ScheduleWindowsAsync(
        Guid employeeId, DateOnly from, DateOnly to, IReadOnlyDictionary<Guid, WorkSchedule> scheduleById, CancellationToken cancellationToken)
    {
        var covering = await scheduleAssignments.ListCoveringRangeAsync(employeeId, from, to, cancellationToken);
        return covering
            .Where(a => scheduleById.ContainsKey(a.WorkScheduleId))
            .Select(a => (a, scheduleById[a.WorkScheduleId]))
            .ToList();
    }

    /// <summary>Çalışanın çalışma tablosuna göre planlı çalışma günleri (istihdam aralığı içinde), tarih sırasıyla.</summary>
    private static IEnumerable<DateOnly> PlannedDays(
        EmployeeReportRow person, DateOnly from, DateOnly to,
        IReadOnlyList<(EmployeeWorkScheduleAssignment Assignment, WorkSchedule Schedule)> windows, WorkSchedule? defaultSchedule)
    {
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            if (person.HireDate is { } hired && date < hired || person.TerminationDate is { } left && date > left)
            {
                continue;
            }

            var effective = windows
                .Where(w => w.Assignment.EffectiveFrom <= date && (w.Assignment.EffectiveTo is null || w.Assignment.EffectiveTo >= date))
                .OrderByDescending(w => w.Assignment.EffectiveFrom)
                .Select(w => w.Schedule)
                .FirstOrDefault() ?? defaultSchedule;
            if (effective is not null && effective.GetDay(date.DayOfWeek).IsWorkingDay)
            {
                yield return date;
            }
        }
    }
}

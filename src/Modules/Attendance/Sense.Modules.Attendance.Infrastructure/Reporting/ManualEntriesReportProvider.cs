using Microsoft.Extensions.Localization;
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
/// Elle Girilen Kayıtlar (Z4.1b): tarih aralığında elle/talep yoluyla girilmiş okutmalar. Denetim amaçlı olarak yumuşak
/// silinmiş elle kayıtlar da <c>Silindi</c> sütunuyla (silen kullanıcı + gerekçe) listelenir. Zaman kiracının yerel saatindedir.
/// Satır kümesi attendance.daily.view veri kapsamı ∩ birim süzgecidir. from/to zorunlu (en çok 366 gün).
/// </summary>
public sealed class ManualEntriesReportProvider(
    IPunchReportReader punches,
    IDataScopeService dataScope,
    IOrgUnitLookup orgUnits,
    IEmployeeReportReader employees,
    ITenantTimeZoneLookup timeZones,
    IUserProfileLookup users,
    IReportExporter exporter,
    ISimplePdfWriter pdfWriter,
    IStringLocalizer<SharedResource> localizer) : IReportProvider
{
    public const string ReportKey = "attendance.time.manual_entries";
    private const int MaxRangeDays = 366;

    public string Key => ReportKey;

    public string CategoryKey => ReportCategories.Time;

    public string? RequiredPermission => PunchReportSupport.ScopePermission;

    public IReadOnlyList<ReportParameterDescriptor> Parameters { get; } = PunchReportSupport.Parameters(dateRangeRequired: true);

    public async Task<ReportFileResult> GenerateAsync(Guid tenantId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var range = ReportParameters.GetDateRange(parameters)
            ?? throw new InvalidOperationException("Manual entries report requires a valid from/to date range.");
        var from = range.From;
        var to = range.To.DayNumber - from.DayNumber >= MaxRangeDays ? from.AddDays(MaxRangeDays - 1) : range.To;

        var people = await PunchReportSupport.LoadPeopleAsync(dataScope, orgUnits, employees, tenantId, parameters, cancellationToken);
        var zone = await PunchReportSupport.ZoneAsync(timeZones, tenantId, cancellationToken);
        var entries = await punches.ListManualForRangeAsync(tenantId, from, to, people.Keys.ToList(), cancellationToken);

        var userIds = entries.SelectMany(p => new[] { p.CreatedByUserId, p.DeletedByUserId }).OfType<Guid>().Distinct().ToList();
        var userNames = userIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await users.GetAsync(tenantId, userIds, cancellationToken)).ToDictionary(u => u.Key, u => u.Value.DisplayName);

        ExportColumn[] columns =
        [
            new(localizer.Column("employeeName")),
            new(localizer.Column("workDate"), ExportColumnType.Date),
            new(localizer.Column("occurredAt"), ExportColumnType.DateTime),
            new(localizer.Column("direction")),
            new(localizer.Column("source")),
            new(localizer.Column("enteredBy")),
            new(localizer.Column("reason")),
            new(localizer.Column("deleted")),
            new(localizer.Column("deletedBy")),
            new(localizer.Column("deleteReason")),
        ];
        var data = entries
            .Where(p => people.ContainsKey(p.EmployeeId))
            .OrderBy(p => PunchReportSupport.Name(people, p.EmployeeId), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(p => p.OccurredAtUtc)
            .Select(p => (IReadOnlyList<object?>)new List<object?>
            {
                PunchReportSupport.Name(people, p.EmployeeId), p.WorkDate, PunchReportSupport.ToLocal(p.OccurredAtUtc, zone),
                PunchReportSupport.DirectionText(localizer, p.Direction), PunchReportSupport.SourceText(localizer, p.Source),
                p.CreatedByUserId is { } c ? userNames.GetValueOrDefault(c) : null, p.Reason,
                p.IsDeleted ? localizer["report.enum.Boolean.True"].Value : localizer["report.enum.Boolean.False"].Value,
                p.DeletedByUserId is { } d ? userNames.GetValueOrDefault(d) : null, p.DeleteReason,
            })
            .ToList();

        return PunchReportSupport.Produce(
            parameters, exporter, pdfWriter, localizer, "attendanceManualEntries", AttendanceReportPdfExport.FormatPeriod(from, to), from, columns, data, cancellationToken);
    }
}

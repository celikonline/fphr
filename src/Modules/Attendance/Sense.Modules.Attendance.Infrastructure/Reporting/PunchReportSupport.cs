using Microsoft.Extensions.Localization;
using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Punches;
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
/// Okutma (Punch) tabanlı zaman raporlarının (Elle Girilen Kayıtlar, İçerdekiler, Mola, Ara Kayıtlar) ortak parçaları:
/// kapsamlı çalışan kümesi, kiracı saat dilimi, terminal adları, sütun/ değer etiketleri ve ortak parametre listesi.
/// </summary>
internal static class PunchReportSupport
{
    public const string ScopePermission = "attendance.daily.view";
    public const string TimeFormat = "HH:mm";
    private const string FormatParameterKey = "format";

    public static IReadOnlyList<ReportParameterDescriptor> Parameters(bool dateRangeRequired) =>
    [
        new(ReportParameterKeys.DateRange, ReportParameterKind.DateRange, IsRequired: dateRangeRequired),
        new(ReportParameterKeys.OrgUnitIds, ReportParameterKind.OrgUnits),
        new(FormatParameterKey, ReportParameterKind.Select, Options: ["xlsx", "pdf"]),
    ];

    /// <summary>Kapsam ∩ birim süzgeci ∩ (isteğe bağlı) ayrılanlar; çalışan kimliğine göre sözlük.</summary>
    public static async Task<IReadOnlyDictionary<Guid, EmployeeReportRow>> LoadPeopleAsync(
        IDataScopeService dataScope, IOrgUnitLookup orgUnits, IEmployeeReportReader employees, Guid tenantId,
        IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var (rows, _) = await new ReportEmployeeSet(dataScope, orgUnits, employees).LoadAsync(tenantId, ScopePermission, parameters, cancellationToken);
        return rows.ToDictionary(r => r.EmployeeId);
    }

    public static async Task<TimeZoneInfo> ZoneAsync(ITenantTimeZoneLookup timeZones, Guid tenantId, CancellationToken cancellationToken)
    {
        var id = await timeZones.FindTimeZoneIdAsync(tenantId, cancellationToken);
        return id is not null && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : TimeZoneInfo.Utc;
    }

    public static DateTime ToLocal(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);

    public static async Task<IReadOnlyDictionary<Guid, string>> TerminalNamesAsync(ITerminalRepository terminals, CancellationToken cancellationToken) =>
        (await terminals.ListTerminalsAsync(cancellationToken)).ToDictionary(t => t.Id, t => t.Name);

    public static string Name(IReadOnlyDictionary<Guid, EmployeeReportRow> people, Guid employeeId) =>
        people.TryGetValue(employeeId, out var p) ? $"{p.FirstName} {p.LastName}".Trim() : string.Empty;

    public static string? SourceText(IStringLocalizer localizer, PunchSource source) => localizer.EnumText<PunchSource>(source);

    public static string? DirectionText(IStringLocalizer localizer, PunchDirection direction) => localizer.EnumText<PunchDirection>(direction);

    public static string? TerminalName(IReadOnlyDictionary<Guid, string> names, Guid? terminalId) =>
        terminalId is { } id ? names.GetValueOrDefault(id) : null;

    /// <summary>Ortak çıkış: "format=pdf" ise PDF, aksi halde Excel (boş satır kümesi yalnız başlıklı dosya).</summary>
    public static ReportFileResult Produce(
        IReadOnlyDictionary<string, string> parameters, IReportExporter exporter, ISimplePdfWriter pdfWriter, IStringLocalizer localizer,
        string sheetKey, string periodText, DateOnly stemDate, IReadOnlyList<ExportColumn> columns, IReadOnlyList<IReadOnlyList<object?>> rows,
        CancellationToken cancellationToken)
    {
        var sheet = localizer.Sheet(sheetKey);
        return AttendanceReportPdfExport.Produce(parameters, exporter, pdfWriter, sheet, $"{sheet} {stemDate:yyyy-MM-dd}", columns, rows, periodText, cancellationToken);
    }

    /// <summary>Tek gün / aralık (en çok <see cref="DailyStatusReportSupport.MaxDays"/> gün) / bugün çözümü; kapanış günü de döner.</summary>
    public static async Task<(DateOnly From, DateOnly To)> ResolveRangeAsync(
        ITenantLocalClock clock, Guid tenantId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var days = await DailyStatusReportSupport.ResolveDaysAsync(clock, tenantId, parameters, cancellationToken);
        return (days[0], days[^1]);
    }
}

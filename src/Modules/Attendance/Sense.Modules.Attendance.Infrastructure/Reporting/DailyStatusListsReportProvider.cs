using Microsoft.Extensions.Localization;
using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Infrastructure;

namespace Sense.Modules.Attendance.Infrastructure.Reporting;

/// <summary>
/// Günlük durum listeleri (Z4.1): gelenler, geç gelenler, erken çıkanlar, devamsızlar, fazla mesai, eksik mesai, izinliler
/// ve hesaplanamayanlar tek raporda. "status" parametresi <see cref="DailyStatus"/> adıdır (camelCase; büyük/küçük harf
/// duyarsız) ve Reporting'in doğrulama hattında duyurulmayan ek anahtar olarak okunur (tıpkı "format" gibi).
/// Veri kapsamı (attendance.daily.view) günlük sorguda uygulanır.
/// </summary>
public sealed class DailyStatusListsReportProvider(
    IDailyAttendanceQuery query,
    ITenantLocalClock clock,
    IReportExporter exporter,
    ISimplePdfWriter pdfWriter,
    IStringLocalizer<Sense.Shared.Infrastructure.SharedResource> localizer) : IReportProvider
{
    public const string ReportKey = "attendance.time.daily_status";
    public const string StatusParameterKey = "status";
    private const string FormatParameterKey = "format";

    public string Key => ReportKey;

    public string CategoryKey => ReportCategories.Time;

    public string? RequiredPermission => "attendance.daily.view";

    public IReadOnlyList<ReportParameterDescriptor> Parameters { get; } =
    [
        .. DailyStatusReportSupport.DateParameters,
        new(StatusParameterKey, ReportParameterKind.Select, IsRequired: true, Options: StatusOptions),
        new(FormatParameterKey, ReportParameterKind.Select, Options: ["xlsx", "pdf"]),
    ];

    private static readonly string[] StatusOptions =
        [.. Enum.GetValues<DailyStatus>().Select(s => char.ToLowerInvariant(s.ToString()[0]) + s.ToString()[1..])];

    public async Task<ReportFileResult> GenerateAsync(Guid tenantId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var status = ResolveStatus(parameters);
        var days = await DailyStatusReportSupport.ResolveDaysAsync(clock, tenantId, parameters, cancellationToken);
        var rows = await DailyStatusReportSupport.LoadAsync(query, tenantId, days, status, parameters, cancellationToken);
        return DailyStatusReportSupport.Produce(parameters, exporter, pdfWriter, localizer, "attendanceDailyStatus", days, rows, cancellationToken);
    }

    private static DailyStatus ResolveStatus(IReadOnlyDictionary<string, string> parameters)
    {
        if (parameters.TryGetValue(StatusParameterKey, out var value)
            && Enum.TryParse<DailyStatus>(value?.Trim(), ignoreCase: true, out var status)
            && Enum.IsDefined(status))
        {
            return status;
        }

        throw new InvalidOperationException("Daily status report requires a valid 'status' parameter (incalculable|arrived|late|earlyLeave|absent|overtime|undertime|onLeave).");
    }
}

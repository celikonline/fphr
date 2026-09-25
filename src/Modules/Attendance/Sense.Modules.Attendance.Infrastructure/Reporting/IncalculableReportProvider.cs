using Microsoft.Extensions.Localization;
using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Infrastructure;

namespace Sense.Modules.Attendance.Infrastructure.Reporting;

/// <summary>
/// Hesaplanamayanlar (Z4.1): seçilen günde puantajı hesaplanamayan (geçersiz/eksik okutma, çözülmemiş uyuşmazlık)
/// çalışanlar. Günlük puantaj sorgusunun <see cref="DailyStatus.Incalculable"/> listesiyle aynı kümedir; kapsam
/// (attendance.daily.view) sorguda uygulanır. Parametreler: tarih ("date" ya da from/to), birimler, isteğe bağlı "format=pdf".
/// </summary>
public sealed class IncalculableReportProvider(
    IDailyAttendanceQuery query,
    ITenantLocalClock clock,
    IReportExporter exporter,
    ISimplePdfWriter pdfWriter,
    IStringLocalizer<Sense.Shared.Infrastructure.SharedResource> localizer) : IReportProvider
{
    public const string ReportKey = "attendance.time.incalculable";

    public string Key => ReportKey;

    public string CategoryKey => ReportCategories.Time;

    public string? RequiredPermission => "attendance.daily.view";

    public IReadOnlyList<ReportParameterDescriptor> Parameters => DailyStatusReportSupport.DateParameters;

    public async Task<ReportFileResult> GenerateAsync(Guid tenantId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var days = await DailyStatusReportSupport.ResolveDaysAsync(clock, tenantId, parameters, cancellationToken);
        var rows = await DailyStatusReportSupport.LoadAsync(query, tenantId, days, DailyStatus.Incalculable, parameters, cancellationToken);
        return DailyStatusReportSupport.Produce(parameters, exporter, pdfWriter, localizer, "attendanceIncalculable", days, rows, cancellationToken);
    }
}

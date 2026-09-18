using Sense.Modules.Analytics.Contracts;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Reporting.Contracts;

namespace Sense.Modules.Identity.Application.UnitAuthorizations;

/// <summary>
/// "Birim yöneticisi" izin kümeleri (11.3.01): bir UnitAuthorization, rol ve override'lara ek olarak bu izinleri
/// yalnız yetkili birim + aktif alt birimleri kapsamında verir. "Kendin oluştur" (<see cref="CustomReportBuild"/>) hiçbir kümede yoktur.
/// </summary>
public static class UnitManagerGrants
{
    /// <summary>Eski/yayılmamış anahtar; hiçbir uç tarafından tüketilmiyordu (G-10-18). Geriye dönük uyumluluk için
    /// <see cref="UnitReports"/>'a dahil tutulur, ama Reporting/Analytics artık kendi izin anahtarlarını kontrol eder.</summary>
    public const string UnitReportsRun = "reports.unit.run";

    /// <summary>Asla birim yetkisiyle verilmez.</summary>
    public const string CustomReportBuild = "reports.custom.build";

    public static readonly IReadOnlyList<string> Base =
    [
        EmployeePermissions.EmployeesReadKey,
        EmployeePermissions.EmployeesCreateKey,
        EmployeePermissions.EmployeesUpdateKey,
        EmployeePermissions.DirectoryViewKey,
        EmployeePermissions.OrgChartViewKey,
    ];

    public static readonly IReadOnlyList<string> SalaryAndPayments = [EmployeePermissions.SalaryViewKey];

    /// <summary>
    /// G-10-18: "kendi birim raporları" anahtarı yalnız <see cref="UnitReportsRun"/>'ı veriyordu, ama Reporting'in
    /// (<see cref="ReportingPermissions"/>) ve Analytics'in (<see cref="AnalyticsPermissions"/>) uçları o anahtarı
    /// hiç kontrol etmiyor - kapsamlı erişim <see cref="Sense.Shared.Contracts.Security.IDataScopeService"/>'in tam
    /// bu izin dizeleriyle sorgulanmasına dayanıyor (bkz. <c>GetWorkforceKpisHandler</c>,
    /// <c>ReportEmployeeSet.LoadAsync</c>). Bu yüzden birim yöneticisine gerçek uçların kontrol ettiği izinler
    /// veriliyor; <see cref="UnitReportsRun"/> geriye dönük uyumluluk için kalıyor.
    /// </summary>
    public static readonly IReadOnlyList<string> UnitReports =
    [
        UnitReportsRun,
        AnalyticsPermissions.KpiView.Key,
        ReportingPermissions.CatalogView.Key,
        ReportingPermissions.ExecutionsGenerate.Key,
        ReportingPermissions.MyExecutions.Key,
    ];

    public static IReadOnlyList<string> For(bool canViewSalaryAndPayments, bool canViewUnitReports) =>
    [
        .. Base,
        .. canViewSalaryAndPayments ? SalaryAndPayments : [],
        .. canViewUnitReports ? UnitReports : [],
    ];
}

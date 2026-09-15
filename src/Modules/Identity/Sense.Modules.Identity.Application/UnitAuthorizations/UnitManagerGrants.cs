using Sense.Modules.Employee.Contracts;

namespace Sense.Modules.Identity.Application.UnitAuthorizations;

/// <summary>
/// "Birim yöneticisi" izin kümeleri (11.3.01): bir UnitAuthorization, rol ve override'lara ek olarak bu izinleri
/// yalnız yetkili birim + aktif alt birimleri kapsamında verir. "Kendin oluştur" (<see cref="CustomReportBuild"/>) hiçbir kümede yoktur.
/// </summary>
public static class UnitManagerGrants
{
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

    public static readonly IReadOnlyList<string> UnitReports = [UnitReportsRun];

    public static IReadOnlyList<string> For(bool canViewSalaryAndPayments, bool canViewUnitReports) =>
    [
        .. Base,
        .. canViewSalaryAndPayments ? SalaryAndPayments : [],
        .. canViewUnitReports ? UnitReports : [],
    ];
}

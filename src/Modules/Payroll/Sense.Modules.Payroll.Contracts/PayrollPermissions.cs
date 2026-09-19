using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Payroll.Contracts;

/// <summary>
/// Payroll module permissions (11.3.7 "Roller ve yetkiler"). This slice (P0/P1) only adds the three keys P0 scopes:
/// trace visibility, profile management and period recalculation. The rest of the 11.3.07-bordro.md permission list
/// (payroll.export.external, payroll.sheet.export, payroll.union.manage, payroll.courtorder.manage,
/// payroll.payments.view, payroll.payslip.acknowledge) comes in later packages (P2+) alongside the features they gate.
/// </summary>
public static class PayrollPermissions
{
    private const string Module = "payroll";
    private const string Group = "Payroll";

    public static readonly Permission TraceView = new(
        "payroll.trace.view", Module, "View Calculation Trace", Group, "View a payslip's step-by-step calculation trace (Hesap turnusolu)");

    public static readonly Permission ProfileManage = new(
        "payroll.profile.manage", Module, "Manage Payroll Profiles", Group, "Create and edit employees' payroll profiles");

    public static readonly Permission PeriodRecalculate = new(
        "payroll.period.recalculate", Module, "Recalculate Payroll Period", Group, "Recalculate payslips for an open payroll period");

    /// <summary>senseik-019 2c: icra/nafaka kesinti emirlerini açma/kapatma.</summary>
    public static readonly Permission CourtOrderManage = new(
        "payroll.courtorder.manage", Module, "Manage Garnishment Orders", Group, "Create and close employees' garnishment (icra/nafaka) orders");

    /// <summary>senseik-019 3b/3c/3d: e-Bildirge/APHB, banka ödeme talimatı ve muhasebe fişi dosyalarını indirme.
    /// "payroll.declarations.*" içeren mevcut muhasebe/bordro sistem rolüne (SystemRoleDefinitions) otomatik dahildir.</summary>
    public static readonly Permission DeclarationsExport = new(
        "payroll.declarations.export", Module, "Export Payroll Declarations", Group, "Download APHB/e-Bildirge, bank payment and accounting voucher files");

    public static IEnumerable<Permission> All => [TraceView, ProfileManage, PeriodRecalculate, CourtOrderManage, DeclarationsExport];
}

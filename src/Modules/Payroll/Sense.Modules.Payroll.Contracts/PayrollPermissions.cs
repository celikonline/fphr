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
    private const string Group = "Bordro";

    public static readonly Permission TraceView = new(
        "payroll.trace.view", Module, "Hesaplama Dökümünü Görüntüleme", Group, "Bir bordronun adım adım hesaplama dökümünü (hesap turnusolu) görüntüleme");

    public static readonly Permission ProfileManage = new(
        "payroll.profile.manage", Module, "Bordro Profili Yönetimi", Group, "Çalışanların bordro profillerini oluşturma ve düzenleme");

    public static readonly Permission PeriodRecalculate = new(
        "payroll.period.recalculate", Module, "Bordro Dönemini Yeniden Hesaplama", Group, "Açık bir bordro dönemi için bordroları yeniden hesaplama");

    /// <summary>senseik-011 (F-06 "İşten ayrılış sihirbazı"): read-only kıdem/ihbar/kullanılmamış izin ücreti tahmini.</summary>
    public static readonly Permission SeveranceEstimate = new(
        "payroll.severance.estimate", Module, "Estimate Severance", Group, "View a read-only severance/notice estimate for an employee");

    /// <summary>senseik-019 2c: icra/nafaka kesinti emirlerini açma/kapatma.</summary>
    public static readonly Permission PeriodApprove = new(
        "payroll.period.approve", Module, "payroll.period.approve", Group, "Approve a calculated payroll period and mark it as paid");

    public static readonly Permission PeriodLock = new(
        "payroll.period.lock", Module, "payroll.period.lock", Group, "Lock a paid payroll period so it can no longer change");

    public static readonly Permission PeriodReopen = new(
        "payroll.period.reopen", Module, "payroll.period.reopen", Group, "Reopen an approved, paid or locked payroll period with a written reason");

    public static readonly Permission AdjustmentManage = new(
        "payroll.adjustment.manage", Module, "payroll.adjustment.manage", Group, "Add and remove one-off taxable earnings (bonus, commission) for an employee in an open payroll period");

    public static readonly Permission BankFileExport = new(
        "payroll.bankfile.export", Module, "payroll.bankfile.export", Group, "Download the bank payment file of an approved payroll period (contains employees' IBANs)");

    public static readonly Permission ExitSettlementManage = new(
        "payroll.exitsettlement.manage", Module, "payroll.exitsettlement.manage", Group, "Add, edit and remove lines of an employee's exit settlement (ikale bedeli, ek ödeme, çıkış ayı ücreti) and recalculate its net");

    public static readonly Permission CourtOrderManage = new(
        "payroll.courtorder.manage", Module, "payroll.courtorder.manage", Group, "Create and close employees' garnishment (icra/nafaka) orders");

    /// <summary>senseik-019 3b/3c/3d: e-Bildirge/APHB, banka ödeme talimatı ve muhasebe fişi dosyalarını indirme.
    /// "payroll.declarations.*" içeren mevcut muhasebe/bordro sistem rolüne (SystemRoleDefinitions) otomatik dahildir.</summary>
    public static readonly Permission DeclarationsExport = new(
        "payroll.declarations.export", Module, "payroll.declarations.export", Group, "Download APHB/e-Bildirge, bank payment and accounting voucher files");

    /// <summary>11.3.07-bordro.md "Sendika & TİS" (#58, P5): manage unions and employees' union/TİS membership + dues.</summary>
    public static readonly Permission UnionManage = new(
        "payroll.union.manage", Module, "payroll.union.manage", Group, "Manage unions and employees' union/collective-agreement membership and dues");

    public static readonly Permission PayslipImport = new(
        "payroll.payslip.import", Module, "payroll.payslip.import", Group, "Bulk-upload payslip PDF/zip files and match them to employees by registration number");

    public static readonly Permission MyPayslip = new(
        "payroll.my.payslip", Module, "payroll.my.payslip", Group, "View, acknowledge and object to one's own payslips");

    public static IEnumerable<Permission> All => [TraceView, ProfileManage, PeriodRecalculate, SeveranceEstimate, PeriodApprove, PeriodLock, PeriodReopen, BankFileExport, AdjustmentManage, ExitSettlementManage, CourtOrderManage, DeclarationsExport, UnionManage, PayslipImport, MyPayslip];
}

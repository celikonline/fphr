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

    public static IEnumerable<Permission> All => [TraceView, ProfileManage, PeriodRecalculate];
}

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

    /// <summary>senseik-011 (F-06 "İşten ayrılış sihirbazı"): read-only kıdem/ihbar/kullanılmamış izin ücreti tahmini.</summary>
    public static readonly Permission SeveranceEstimate = new(
        "payroll.severance.estimate", Module, "Estimate Severance", Group, "View a read-only severance/notice estimate for an employee");

    public static IEnumerable<Permission> All => [TraceView, ProfileManage, PeriodRecalculate, SeveranceEstimate];
}

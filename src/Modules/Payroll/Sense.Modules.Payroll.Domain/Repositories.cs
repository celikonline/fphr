using Sense.Modules.Payroll.Domain.Adjustments;
using Sense.Modules.Payroll.Domain.Closing;
using Sense.Modules.Payroll.Domain.ExitSettlements;
using Sense.Modules.Payroll.Domain.Garnishments;
using Sense.Modules.Payroll.Domain.Incentives;
using Sense.Modules.Payroll.Domain.PayItems;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Modules.Payroll.Domain.Profiles;
using Sense.Modules.Payroll.Domain.Unions;

namespace Sense.Modules.Payroll.Domain;

public interface IPayrollPeriodRepository
{
    void Add(PayrollPeriod period);

    Task<PayrollPeriod?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PayrollPeriod?> FindByYearMonthAsync(int year, int month, CancellationToken cancellationToken = default);

    /// <summary>Every currently Open period (ChangeDetectionRun scans these, P3).</summary>
    Task<IReadOnlyList<PayrollPeriod>> ListOpenAsync(CancellationToken cancellationToken = default);

    /// <summary>Open periods whose calendar month is on or after <paramref name="effectiveFrom"/>'s month (the "Değişti"
    /// mechanism only flags periods that already exist; a period opened later picks up current data on its first run).</summary>
    Task<IReadOnlyList<PayrollPeriod>> ListOpenCoveringOrAfterAsync(DateOnly effectiveFrom, CancellationToken cancellationToken = default);

    /// <summary>Every period the tenant has ever opened, newest first (faz-07 tur 1: "Dönem listesi" read-only screen).</summary>
    Task<IReadOnlyList<PayrollPeriod>> ListAllAsync(CancellationToken cancellationToken = default);
}

public interface IPayslipRepository
{
    void Add(Payslip payslip);

    Task<Payslip?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Payslip?> FindByEmployeeAndPeriodAsync(Guid employeeId, Guid payrollPeriodId, CancellationToken cancellationToken = default);

    /// <summary>Every payslip of one employee across periods (employee card "Bordro" tab).</summary>
    Task<IReadOnlyList<Payslip>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>Every payslip of one period (ChangeDetectionRun scan, P3).</summary>
    Task<IReadOnlyList<Payslip>> ListByPeriodAsync(Guid payrollPeriodId, CancellationToken cancellationToken = default);

    /// <summary>E2 reminder: published but not yet acknowledged payslips, of one period or (null) all periods, optionally
    /// only those published on/after <paramref name="publishedSince"/>.</summary>
    Task<IReadOnlyList<Payslip>> ListPublishedUnacknowledgedAsync(
        Guid? payrollPeriodId, DateTimeOffset? publishedSince, CancellationToken cancellationToken = default);
}

/// <summary>TK-030 kabul kriteri 2: e-pusula tebliğ kanıtı (görüntüleme/onay) - bkz. <see cref="PayslipDelivery"/>.</summary>
public interface IPayslipDeliveryRepository
{
    void Add(PayslipDelivery delivery);

    Task<PayslipDelivery?> FindByPayslipIdAsync(Guid payslipId, CancellationToken cancellationToken = default);

    /// <summary>Every delivery record of one employee's own payslips - "Bordro Pusulam" listesi tek sorguda
    /// Viewed/Acknowledged bilgisini eklesin diye (N+1 yok).</summary>
    Task<IReadOnlyList<PayslipDelivery>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);
}

public interface IPayslipCalculationTraceRepository
{
    void Add(PayslipCalculationTrace trace);

    Task<PayslipCalculationTrace?> FindLatestByPayslipIdAsync(Guid payslipId, CancellationToken cancellationToken = default);
}

public interface IPayslipDirtyFlagRepository
{
    void Add(PayslipDirtyFlag flag);

    /// <summary>The one active (unresolved) flag for (period, employee), if any.</summary>
    Task<PayslipDirtyFlag?> FindActiveAsync(Guid payrollPeriodId, Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>Every active flag in a period, for the "Değişenler" filter (GET .../dirty-flags) and RecalculateDirty.</summary>
    Task<IReadOnlyList<PayslipDirtyFlag>> ListActiveByPeriodAsync(Guid payrollPeriodId, CancellationToken cancellationToken = default);
}

public interface IChangeDetectionRunRepository
{
    void Add(ChangeDetectionRun run);
}

public interface IEmployeePayrollProfileRepository
{
    void Add(EmployeePayrollProfile profile);

    void Remove(EmployeePayrollProfile profile);

    Task<EmployeePayrollProfile?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Every version of the employee's profile, newest ValidFrom first.</summary>
    Task<IReadOnlyList<EmployeePayrollProfile>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>The version in effect on <paramref name="asOf"/>, if any.</summary>
    Task<EmployeePayrollProfile?> FindCurrentAsync(Guid employeeId, DateOnly asOf, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every profile version active tenant-wide on <paramref name="asOf"/> (senseik-010/F-03: the Organizasyon/Şirket
    /// profil ekranı's read-only "aktif teşvik türleri" summary aggregates across all employees, not just one).
    /// </summary>
    Task<IReadOnlyList<EmployeePayrollProfile>> ListActiveAsOfAsync(DateOnly asOf, CancellationToken cancellationToken = default);
}

/// <summary>senseik-019 2c: icra/nafaka kesinti emirleri.</summary>
public interface IGarnishmentOrderRepository
{
    void Add(GarnishmentOrder order);

    Task<GarnishmentOrder?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Açık (kapanmamış) emirler - PayrollPeriodCalculationService'in her dönem uyguladığı liste, oluşturulma
    /// sırasına göre (FIFO, GarnishmentCalculator'ın icra havuzu paylaşımı için).</summary>
    Task<IReadOnlyList<GarnishmentOrder>> ListActiveByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>Çalışanın kapalılar dahil tüm emirleri, en yeni önce ("Kesintiler" ekranı).</summary>
    Task<IReadOnlyList<GarnishmentOrder>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);
}

public interface IUnionRepository
{
    void Add(Union union);

    Task<Union?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Union>> ListAsync(CancellationToken cancellationToken = default);
}

public interface IEmployeeUnionMembershipRepository
{
    void Add(EmployeeUnionMembership membership);

    void Remove(EmployeeUnionMembership membership);

    Task<EmployeeUnionMembership?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Every membership record of the employee, newest MembershipDate first.</summary>
    Task<IReadOnlyList<EmployeeUnionMembership>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>The membership in effect on <paramref name="asOf"/> (MembershipDate ≤ asOf ≤ EndDate/open), if any -
    /// used by <c>PayrollPeriodCalculationService</c> to resolve the period's dues via <c>UnionDuesCalculator</c>.</summary>
    Task<EmployeeUnionMembership?> FindCurrentAsync(Guid employeeId, DateOnly asOf, CancellationToken cancellationToken = default);
}

/// <summary>"Çıkış hesabı": frozen kıdem/ihbar/izin amounts written when an employee is terminated.</summary>
public interface IExitSettlementRepository
{
    void Add(ExitSettlement settlement);

    /// <summary>A settlement with its lines.</summary>
    Task<ExitSettlement?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The settlement of one exit (one per employee and termination date; a rehire + second exit gets its own).</summary>
    Task<ExitSettlement?> FindByEmployeeAndDateAsync(Guid employeeId, DateOnly terminationDate, CancellationToken cancellationToken = default);

    /// <summary>Every settlement of the employee, newest termination first.</summary>
    Task<IReadOnlyList<ExitSettlement>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);
}

/// <summary>The closing checklist of a payroll period.</summary>
public interface IPeriodClosingCheckRepository
{
    void Add(PeriodClosingCheck check);

    /// <summary>Every check of the period (empty until the checklist is first opened).</summary>
    Task<IReadOnlyList<PeriodClosingCheck>> ListByPeriodAsync(Guid periodId, CancellationToken cancellationToken = default);
}

/// <summary>One-off taxable earnings (prim, komisyon) added to a payroll month.</summary>
public interface IPayrollAdjustmentRepository
{
    void Add(PayrollAdjustment adjustment);

    void Remove(PayrollAdjustment adjustment);

    Task<PayrollAdjustment?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Every adjustment of one employee in one month (what the payroll run adds to that employee's gross).</summary>
    Task<IReadOnlyList<PayrollAdjustment>> ListByEmployeeAndMonthAsync(Guid employeeId, int year, int month, CancellationToken cancellationToken = default);

    /// <summary>Every adjustment of one month, all employees (the period's "Ek kazançlar" list).</summary>
    Task<IReadOnlyList<PayrollAdjustment>> ListByMonthAsync(int year, int month, CancellationToken cancellationToken = default);
}

/// <summary>PAY-FORMULA: tenant-wide pay item (ödeme türü) definitions.</summary>
public interface IPayItemDefinitionRepository
{
    void Add(PayItemDefinition definition);

    Task<PayItemDefinition?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Definitions ordered by code; inactive ones only when <paramref name="includeInactive"/>.</summary>
    Task<IReadOnlyList<PayItemDefinition>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PayItemDefinition>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}

/// <summary>PAY-FORMULA: pay item assignments to employees.</summary>
public interface IEmployeePayItemAssignmentRepository
{
    void Add(EmployeePayItemAssignment assignment);

    void Remove(EmployeePayItemAssignment assignment);

    Task<EmployeePayItemAssignment?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Every assignment of one employee, newest start first.</summary>
    Task<IReadOnlyList<EmployeePayItemAssignment>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>Assignments of the given employees (batch limit check), any range.</summary>
    Task<IReadOnlyList<EmployeePayItemAssignment>> ListByEmployeesAsync(IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default);

    /// <summary>Assignments covering the month starting <paramref name="monthStart"/>, all employees.</summary>
    Task<IReadOnlyList<EmployeePayItemAssignment>> ListCoveringMonthAsync(DateOnly monthStart, CancellationToken cancellationToken = default);

    /// <summary>Every assignment of one definition that has not ended before <paramref name="notEndedBefore"/>.</summary>
    Task<IReadOnlyList<EmployeePayItemAssignment>> ListByDefinitionAsync(Guid definitionId, DateOnly notEndedBefore, CancellationToken cancellationToken = default);
}

/// <summary>PAY-INCENTIVE: tenant-wide incentive definitions.</summary>
public interface IIncentiveDefinitionRepository
{
    void Add(IncentiveDefinition definition);

    Task<IncentiveDefinition?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default);

    Task<bool> TemplateExistsAsync(string templateKey, CancellationToken cancellationToken = default);

    /// <summary>Definitions ordered by code; inactive ones only when <paramref name="includeInactive"/>.</summary>
    Task<IReadOnlyList<IncentiveDefinition>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IncentiveDefinition>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}

/// <summary>PAY-INCENTIVE: incentive assignments to employees.</summary>
public interface IEmployeeIncentiveAssignmentRepository
{
    void Add(EmployeeIncentiveAssignment assignment);

    void Remove(EmployeeIncentiveAssignment assignment);

    Task<EmployeeIncentiveAssignment?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Every assignment of one employee, newest start first.</summary>
    Task<IReadOnlyList<EmployeeIncentiveAssignment>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>Assignments of the given employees (overlap and max-months checks), any range.</summary>
    Task<IReadOnlyList<EmployeeIncentiveAssignment>> ListByEmployeesAsync(IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default);

    /// <summary>Assignments covering the month starting <paramref name="monthStart"/>, all employees.</summary>
    Task<IReadOnlyList<EmployeeIncentiveAssignment>> ListCoveringMonthAsync(DateOnly monthStart, CancellationToken cancellationToken = default);

    /// <summary>Every assignment of one definition that has not ended before <paramref name="notEndedBefore"/>.</summary>
    Task<IReadOnlyList<EmployeeIncentiveAssignment>> ListByDefinitionAsync(Guid definitionId, DateOnly notEndedBefore, CancellationToken cancellationToken = default);
}

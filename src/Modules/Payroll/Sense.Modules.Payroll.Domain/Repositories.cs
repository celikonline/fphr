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

    /// <summary>Every payslip of one period (ChangeDetectionRun scan, P3).</summary>
    Task<IReadOnlyList<Payslip>> ListByPeriodAsync(Guid payrollPeriodId, CancellationToken cancellationToken = default);
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

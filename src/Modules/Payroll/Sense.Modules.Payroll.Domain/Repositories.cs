using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Modules.Payroll.Domain.Profiles;

namespace Sense.Modules.Payroll.Domain;

public interface IPayrollPeriodRepository
{
    void Add(PayrollPeriod period);

    Task<PayrollPeriod?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PayrollPeriod?> FindByYearMonthAsync(int year, int month, CancellationToken cancellationToken = default);
}

public interface IPayslipRepository
{
    void Add(Payslip payslip);

    Task<Payslip?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Payslip?> FindByEmployeeAndPeriodAsync(Guid employeeId, Guid payrollPeriodId, CancellationToken cancellationToken = default);
}

public interface IPayslipCalculationTraceRepository
{
    void Add(PayslipCalculationTrace trace);

    Task<PayslipCalculationTrace?> FindLatestByPayslipIdAsync(Guid payslipId, CancellationToken cancellationToken = default);
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

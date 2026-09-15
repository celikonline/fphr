using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Domain.Periods;

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

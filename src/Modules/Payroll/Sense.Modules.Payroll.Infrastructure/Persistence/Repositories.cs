using Microsoft.EntityFrameworkCore;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Domain.Periods;

namespace Sense.Modules.Payroll.Infrastructure.Persistence;

public sealed class PayrollPeriodRepository(PayrollDbContext context) : IPayrollPeriodRepository
{
    public void Add(PayrollPeriod period) => context.Periods.Add(period);

    public async Task<PayrollPeriod?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.Periods.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<PayrollPeriod?> FindByYearMonthAsync(int year, int month, CancellationToken cancellationToken = default) =>
        await context.Periods.FirstOrDefaultAsync(p => p.Year == year && p.Month == month, cancellationToken);
}

public sealed class PayslipRepository(PayrollDbContext context) : IPayslipRepository
{
    public void Add(Payslip payslip) => context.Payslips.Add(payslip);

    public async Task<Payslip?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.Payslips.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<Payslip?> FindByEmployeeAndPeriodAsync(Guid employeeId, Guid payrollPeriodId, CancellationToken cancellationToken = default) =>
        await context.Payslips.FirstOrDefaultAsync(p => p.EmployeeId == employeeId && p.PayrollPeriodId == payrollPeriodId, cancellationToken);
}

public sealed class PayslipCalculationTraceRepository(PayrollDbContext context) : IPayslipCalculationTraceRepository
{
    public void Add(PayslipCalculationTrace trace) => context.CalculationTraces.Add(trace);

    public async Task<PayslipCalculationTrace?> FindLatestByPayslipIdAsync(Guid payslipId, CancellationToken cancellationToken = default) =>
        await context.CalculationTraces
            .Where(t => t.PayslipId == payslipId)
            .OrderByDescending(t => t.Version)
            .FirstOrDefaultAsync(cancellationToken);
}

using Microsoft.EntityFrameworkCore;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Modules.Payroll.Domain.Profiles;

namespace Sense.Modules.Payroll.Infrastructure.Persistence;

public sealed class PayrollPeriodRepository(PayrollDbContext context) : IPayrollPeriodRepository
{
    public void Add(PayrollPeriod period) => context.Periods.Add(period);

    public async Task<PayrollPeriod?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.Periods.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<PayrollPeriod?> FindByYearMonthAsync(int year, int month, CancellationToken cancellationToken = default) =>
        await context.Periods.FirstOrDefaultAsync(p => p.Year == year && p.Month == month, cancellationToken);

    public async Task<IReadOnlyList<PayrollPeriod>> ListOpenAsync(CancellationToken cancellationToken = default) =>
        await context.Periods.Where(p => p.Status == PayrollPeriodStatus.Open).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PayrollPeriod>> ListOpenCoveringOrAfterAsync(DateOnly effectiveFrom, CancellationToken cancellationToken = default) =>
        await context.Periods
            .Where(p => p.Status == PayrollPeriodStatus.Open
                && (p.Year > effectiveFrom.Year || (p.Year == effectiveFrom.Year && p.Month >= effectiveFrom.Month)))
            .ToListAsync(cancellationToken);
}

public sealed class PayslipRepository(PayrollDbContext context) : IPayslipRepository
{
    public void Add(Payslip payslip) => context.Payslips.Add(payslip);

    public async Task<Payslip?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.Payslips.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<Payslip?> FindByEmployeeAndPeriodAsync(Guid employeeId, Guid payrollPeriodId, CancellationToken cancellationToken = default) =>
        await context.Payslips.FirstOrDefaultAsync(p => p.EmployeeId == employeeId && p.PayrollPeriodId == payrollPeriodId, cancellationToken);

    public async Task<IReadOnlyList<Payslip>> ListByPeriodAsync(Guid payrollPeriodId, CancellationToken cancellationToken = default) =>
        await context.Payslips.Where(p => p.PayrollPeriodId == payrollPeriodId).ToListAsync(cancellationToken);
}

public sealed class PayslipDirtyFlagRepository(PayrollDbContext context) : IPayslipDirtyFlagRepository
{
    public void Add(PayslipDirtyFlag flag) => context.DirtyFlags.Add(flag);

    public async Task<PayslipDirtyFlag?> FindActiveAsync(Guid payrollPeriodId, Guid employeeId, CancellationToken cancellationToken = default) =>
        await context.DirtyFlags.FirstOrDefaultAsync(
            f => f.PayrollPeriodId == payrollPeriodId && f.EmployeeId == employeeId && f.ClearedAt == null, cancellationToken);

    public async Task<IReadOnlyList<PayslipDirtyFlag>> ListActiveByPeriodAsync(Guid payrollPeriodId, CancellationToken cancellationToken = default) =>
        await context.DirtyFlags.Where(f => f.PayrollPeriodId == payrollPeriodId && f.ClearedAt == null).ToListAsync(cancellationToken);
}

public sealed class ChangeDetectionRunRepository(PayrollDbContext context) : IChangeDetectionRunRepository
{
    public void Add(ChangeDetectionRun run) => context.ChangeDetectionRuns.Add(run);
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

public sealed class EmployeePayrollProfileRepository(PayrollDbContext context) : IEmployeePayrollProfileRepository
{
    public void Add(EmployeePayrollProfile profile) => context.EmployeePayrollProfiles.Add(profile);

    public void Remove(EmployeePayrollProfile profile) => context.EmployeePayrollProfiles.Remove(profile);

    public async Task<EmployeePayrollProfile?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.EmployeePayrollProfiles.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<EmployeePayrollProfile>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        await context.EmployeePayrollProfiles
            .Where(p => p.EmployeeId == employeeId)
            .OrderByDescending(p => p.ValidFrom)
            .ToListAsync(cancellationToken);

    public async Task<EmployeePayrollProfile?> FindCurrentAsync(Guid employeeId, DateOnly asOf, CancellationToken cancellationToken = default) =>
        await context.EmployeePayrollProfiles
            .Where(p => p.EmployeeId == employeeId && p.ValidFrom <= asOf && (p.ValidTo == null || asOf <= p.ValidTo))
            .OrderByDescending(p => p.ValidFrom)
            .FirstOrDefaultAsync(cancellationToken);
}

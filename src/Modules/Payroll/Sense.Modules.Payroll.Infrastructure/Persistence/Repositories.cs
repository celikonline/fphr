using Microsoft.EntityFrameworkCore;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Adjustments;
using Sense.Modules.Payroll.Domain.Closing;
using Sense.Modules.Payroll.Domain.ExitSettlements;
using Sense.Modules.Payroll.Domain.Garnishments;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Modules.Payroll.Domain.Profiles;
using Sense.Modules.Payroll.Domain.Unions;

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

    public async Task<IReadOnlyList<PayrollPeriod>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await context.Periods.OrderByDescending(p => p.Year).ThenByDescending(p => p.Month).ToListAsync(cancellationToken);
}

public sealed class PayslipRepository(PayrollDbContext context) : IPayslipRepository
{
    public void Add(Payslip payslip) => context.Payslips.Add(payslip);

    public async Task<Payslip?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.Payslips.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<Payslip?> FindByEmployeeAndPeriodAsync(Guid employeeId, Guid payrollPeriodId, CancellationToken cancellationToken = default) =>
        await context.Payslips.FirstOrDefaultAsync(p => p.EmployeeId == employeeId && p.PayrollPeriodId == payrollPeriodId, cancellationToken);

    public async Task<IReadOnlyList<Payslip>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        await context.Payslips.Where(p => p.EmployeeId == employeeId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Payslip>> ListByPeriodAsync(Guid payrollPeriodId, CancellationToken cancellationToken = default) =>
        await context.Payslips.Where(p => p.PayrollPeriodId == payrollPeriodId).ToListAsync(cancellationToken);
}

public sealed class PayslipDeliveryRepository(PayrollDbContext context) : IPayslipDeliveryRepository
{
    public void Add(PayslipDelivery delivery) => context.PayslipDeliveries.Add(delivery);

    public async Task<PayslipDelivery?> FindByPayslipIdAsync(Guid payslipId, CancellationToken cancellationToken = default) =>
        await context.PayslipDeliveries.FirstOrDefaultAsync(d => d.PayslipId == payslipId, cancellationToken);

    public async Task<IReadOnlyList<PayslipDelivery>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        await context.PayslipDeliveries.Where(d => d.EmployeeId == employeeId).ToListAsync(cancellationToken);
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

    public async Task<IReadOnlyList<EmployeePayrollProfile>> ListActiveAsOfAsync(DateOnly asOf, CancellationToken cancellationToken = default) =>
        await context.EmployeePayrollProfiles
            .Where(p => p.ValidFrom <= asOf && (p.ValidTo == null || asOf <= p.ValidTo))
            .ToListAsync(cancellationToken);
}

public sealed class GarnishmentOrderRepository(PayrollDbContext context) : IGarnishmentOrderRepository
{
    public void Add(GarnishmentOrder order) => context.GarnishmentOrders.Add(order);

    public async Task<GarnishmentOrder?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.GarnishmentOrders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public async Task<IReadOnlyList<GarnishmentOrder>> ListActiveByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        await context.GarnishmentOrders
            .Where(o => o.EmployeeId == employeeId && o.ClosedDate == null)
            .OrderBy(o => o.StartDate)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<GarnishmentOrder>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        await context.GarnishmentOrders
            .Where(o => o.EmployeeId == employeeId)
            .OrderByDescending(o => o.StartDate)
            .ToListAsync(cancellationToken);
}

public sealed class UnionRepository(PayrollDbContext context) : IUnionRepository
{
    public void Add(Union union) => context.Unions.Add(union);

    public async Task<Union?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.Unions.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Union>> ListAsync(CancellationToken cancellationToken = default) =>
        await context.Unions.OrderBy(u => u.Name).ToListAsync(cancellationToken);
}

public sealed class EmployeeUnionMembershipRepository(PayrollDbContext context) : IEmployeeUnionMembershipRepository
{
    public void Add(EmployeeUnionMembership membership) => context.EmployeeUnionMemberships.Add(membership);

    public void Remove(EmployeeUnionMembership membership) => context.EmployeeUnionMemberships.Remove(membership);

    public async Task<EmployeeUnionMembership?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.EmployeeUnionMemberships.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    public async Task<IReadOnlyList<EmployeeUnionMembership>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        await context.EmployeeUnionMemberships
            .Where(m => m.EmployeeId == employeeId)
            .OrderByDescending(m => m.MembershipDate)
            .ToListAsync(cancellationToken);

    public async Task<EmployeeUnionMembership?> FindCurrentAsync(Guid employeeId, DateOnly asOf, CancellationToken cancellationToken = default) =>
        await context.EmployeeUnionMemberships
            .Where(m => m.EmployeeId == employeeId && m.MembershipDate <= asOf && (m.EndDate == null || asOf <= m.EndDate))
            .OrderByDescending(m => m.MembershipDate)
            .FirstOrDefaultAsync(cancellationToken);
}

public sealed class ExitSettlementRepository(PayrollDbContext context) : IExitSettlementRepository
{
    public void Add(ExitSettlement settlement) => context.ExitSettlements.Add(settlement);

    public async Task<ExitSettlement?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.ExitSettlements.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<ExitSettlement?> FindByEmployeeAndDateAsync(Guid employeeId, DateOnly terminationDate, CancellationToken cancellationToken = default) =>
        await context.ExitSettlements.Include(x => x.Lines).FirstOrDefaultAsync(x => x.EmployeeId == employeeId && x.TerminationDate == terminationDate, cancellationToken);

    public async Task<IReadOnlyList<ExitSettlement>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        await context.ExitSettlements
            .Include(x => x.Lines)
            .Where(x => x.EmployeeId == employeeId)
            .OrderByDescending(x => x.TerminationDate)
            .ToListAsync(cancellationToken);
}

public sealed class PeriodClosingCheckRepository(PayrollDbContext context) : IPeriodClosingCheckRepository
{
    public void Add(PeriodClosingCheck check) => context.ClosingChecks.Add(check);

    public async Task<IReadOnlyList<PeriodClosingCheck>> ListByPeriodAsync(Guid periodId, CancellationToken cancellationToken = default) =>
        await context.ClosingChecks.Where(c => c.PeriodId == periodId).ToListAsync(cancellationToken);
}

public sealed class PayrollAdjustmentRepository(PayrollDbContext context) : IPayrollAdjustmentRepository
{
    public void Add(PayrollAdjustment adjustment) => context.Adjustments.Add(adjustment);

    public void Remove(PayrollAdjustment adjustment) => context.Adjustments.Remove(adjustment);

    public async Task<PayrollAdjustment?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.Adjustments.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<PayrollAdjustment>> ListByEmployeeAndMonthAsync(Guid employeeId, int year, int month, CancellationToken cancellationToken = default) =>
        await context.Adjustments.Where(a => a.EmployeeId == employeeId && a.Year == year && a.Month == month).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PayrollAdjustment>> ListByMonthAsync(int year, int month, CancellationToken cancellationToken = default) =>
        await context.Adjustments.Where(a => a.Year == year && a.Month == month).OrderBy(a => a.Label).ToListAsync(cancellationToken);
}

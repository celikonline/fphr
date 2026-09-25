using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Application.Payslips;

/// <summary>
/// Flushes pending payslip changes inside the running command transaction and turns a lost optimistic-concurrency race
/// (Payslip's xmin token) into <see cref="Domain.PayrollDomainErrors.PayslipConcurrentUpdate"/> (409) instead of an
/// exception. E2 self-service handlers call it before any side effect outside Payroll (the objection's Workflow request).
/// </summary>
public interface IPayslipChangeSaver
{
    Task<Result> SaveAsync(CancellationToken cancellationToken);
}

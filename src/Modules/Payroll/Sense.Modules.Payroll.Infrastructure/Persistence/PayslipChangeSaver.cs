using Microsoft.EntityFrameworkCore;
using Sense.Modules.Payroll.Application.Payslips;
using Sense.Modules.Payroll.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Infrastructure.Persistence;

/// <inheritdoc cref="IPayslipChangeSaver"/>
public sealed class PayslipChangeSaver(PayrollDbContext context) : IPayslipChangeSaver
{
    public async Task<Result> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict(PayrollDomainErrors.PayslipConcurrentUpdate);
        }
    }
}

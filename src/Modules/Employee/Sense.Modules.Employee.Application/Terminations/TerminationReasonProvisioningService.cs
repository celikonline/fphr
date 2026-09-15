using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Terminations;

namespace Sense.Modules.Employee.Application.Terminations;

/// <summary>
/// Seeds the tenant's termination reason catalog from <see cref="TerminationReasonSeed"/> the first time it's needed -
/// there is no per-module tenant-provisioning hook today, so this runs lazily from the first read/write that needs the
/// catalog (mirrors Documents' DocumentTypeProvisioningService, which mirrors Leave's LeaveBalanceService.EnsureDefaultRuleSetAsync).
/// Does not save; callers inside a command handler get it for free from the command pipeline's UnitOfWorkBehaviour,
/// a query handler must call its own IEmployeeUnitOfWork.SaveChangesAsync afterwards.
/// </summary>
public sealed class TerminationReasonProvisioningService(ITerminationReasonRepository reasons)
{
    public async Task EnsureSeededAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (await reasons.AnyForTenantAsync(cancellationToken))
        {
            return;
        }

        foreach (var row in TerminationReasonSeed.SystemDefinitions())
        {
            reasons.Add(TerminationReason.Create(
                tenantId, row.Code, row.Name, row.DefaultSgkExitCode, row.DefaultTerminationType, isSystem: true, row.SortOrder).Value);
        }
    }
}

using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Employee.Application.Terminations;

/// <summary>Termination reason catalog for the wizard's dropdown; includes inactive reasons only when asked.</summary>
[RequiresPermission(EmployeePermissions.EmployeesReadKey)]
public sealed record ListTerminationReasonsQuery(bool IncludeInactive = false) : IQuery<IReadOnlyList<TerminationReasonDto>>;

public sealed class ListTerminationReasonsHandler(
    ITenantContext tenant,
    ITerminationReasonRepository reasons,
    TerminationReasonProvisioningService provisioning,
    IEmployeeUnitOfWork unitOfWork) : IQueryHandler<ListTerminationReasonsQuery, IReadOnlyList<TerminationReasonDto>>
{
    public async Task<Result<IReadOnlyList<TerminationReasonDto>>> Handle(ListTerminationReasonsQuery query, CancellationToken cancellationToken)
    {
        if (!await reasons.AnyForTenantAsync(cancellationToken))
        {
            await provisioning.EnsureSeededAsync(tenant.TenantId, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var list = await reasons.ListAsync(query.IncludeInactive, cancellationToken);
        return Result.Success<IReadOnlyList<TerminationReasonDto>>(list.Select(TerminationReasonDto.From).ToList());
    }
}

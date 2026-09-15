using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Employee.Application.Employees;

/// <summary>
/// Collective-dismissal status of the 30-day window ending on <paramref name="AsOf"/> (today when omitted): what the
/// termination wizard shows before the user submits, so the notification duty is visible in advance.
/// </summary>
[RequiresPermission(EmployeePermissions.EmployeesReadKey)]
public sealed record GetCollectiveDismissalStatusQuery(DateOnly? AsOf = null) : IQuery<CollectiveDismissalEvaluation>;

public sealed class GetCollectiveDismissalStatusHandler(CollectiveDismissalEvaluator evaluator, TimeProvider clock)
    : IQueryHandler<GetCollectiveDismissalStatusQuery, CollectiveDismissalEvaluation>
{
    public async Task<Result<CollectiveDismissalEvaluation>> Handle(GetCollectiveDismissalStatusQuery query, CancellationToken cancellationToken)
    {
        var asOf = query.AsOf ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        return Result.Success(await evaluator.EvaluateAsync(asOf, pendingType: null, cancellationToken));
    }
}

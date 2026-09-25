using Sense.Modules.Workflow.Domain;
using Sense.Modules.Workflow.Domain.Delegations;

namespace Sense.Modules.Workflow.Application.Delegations;

/// <summary>
/// Resolves whose approval tasks an employee may see/decide as a delegate (Z2.2). Delegation is deliberately
/// non-transitive: only delegations whose delegate IS the employee count, so A→B and B→C never lets C act for A.
/// </summary>
public sealed class DelegationResolver(IApprovalDelegationRepository delegations)
{
    /// <summary>Delegators whose delegation to <paramref name="delegateEmployeeId"/> is active on <paramref name="today"/> for <paramref name="requestType"/>. <paramref name="tenantId"/> is enforced by the repository's tenant query filter.</summary>
    public async Task<IReadOnlyList<Guid>> ActiveDelegatorsFor(
        Guid tenantId, Guid delegateEmployeeId, DateOnly today, string requestType, CancellationToken cancellationToken = default)
    {
        var active = await ActiveDelegationsFor(delegateEmployeeId, today, cancellationToken);
        return active
            .Where(d => d.IsActiveOn(today, requestType))
            .Select(d => d.DelegatorEmployeeId)
            .Distinct()
            .ToList();
    }

    /// <summary>Non-revoked, in-range delegations to the delegate (request-type scope not applied - use <see cref="ApprovalDelegation.IsActiveOn"/>).</summary>
    public async Task<IReadOnlyList<ApprovalDelegation>> ActiveDelegationsFor(
        Guid delegateEmployeeId, DateOnly today, CancellationToken cancellationToken = default) =>
        await delegations.ListActiveForDelegateAsync(delegateEmployeeId, today, cancellationToken);

    public static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
}

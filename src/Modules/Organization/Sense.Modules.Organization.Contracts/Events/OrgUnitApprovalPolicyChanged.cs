using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Organization.Contracts.Events;

/// <summary>
/// Published (via Organization's outbox) whenever an org unit's own approval policy assignment for a request type is
/// created, changed or cleared. <paramref name="ApprovalPolicyId"/> null = cleared (the unit now inherits again).
/// Descendant units' effective policy may change too; consumers re-resolve with <see cref="IOrgApprovalPolicyResolver"/>.
/// </summary>
public sealed record OrgUnitApprovalPolicyChanged(Guid TenantId, Guid OrgUnitId, string RequestType, Guid? ApprovalPolicyId)
    : IntegrationEvent(TenantId);

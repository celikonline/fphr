using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Assets.Contracts.Events;

/// <summary>Published when an asset is assigned to an employee.</summary>
public sealed record AssetAssigned(Guid TenantId, Guid AssetId, Guid EmployeeId, DateOnly IssuedAt) : IntegrationEvent(TenantId);

/// <summary>Published when an assigned asset is returned.</summary>
public sealed record AssetReturned(Guid TenantId, Guid AssetId, Guid EmployeeId, DateOnly ReturnedAt) : IntegrationEvent(TenantId);

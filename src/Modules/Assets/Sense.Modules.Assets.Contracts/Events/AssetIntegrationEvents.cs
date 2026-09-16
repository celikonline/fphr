using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Assets.Contracts.Events;

/// <summary>Published when an asset is assigned to an employee.</summary>
public sealed record AssetAssigned(Guid TenantId, Guid AssetId, Guid EmployeeId, DateOnly IssuedAt) : IntegrationEvent(TenantId);

/// <summary>Published when an assigned asset is returned.</summary>
public sealed record AssetReturned(Guid TenantId, Guid AssetId, Guid EmployeeId, DateOnly ReturnedAt) : IntegrationEvent(TenantId);

/// <summary>
/// Published once per still-open (unreturned) asset when its holder is terminated (Employee's EmployeeTerminated).
/// Does not block the termination - it's a durable, timestamped audit trail entry ("zimmet iade edilmeden çıkış")
/// HR can review/act on afterwards, alongside the open-asset count already surfaced at offboarding time
/// (IAssetRepository.CountOpenAssetsAsync / GET assets/employees/{employeeId}).
/// </summary>
public sealed record AssetUnreturnedAtTermination(Guid TenantId, Guid AssetId, Guid EmployeeId) : IntegrationEvent(TenantId);

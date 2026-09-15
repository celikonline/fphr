using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Performance.Contracts.Events;

/// <summary>A review cycle was published (Draft → Published); its forms are now open for evaluators.</summary>
public sealed record ReviewCyclePublished(Guid TenantId, Guid ReviewCycleId) : IntegrationEvent(TenantId);

/// <summary>
/// A participant finished their review (either naturally - every assignment completed - or via ForceComplete).
/// Consumers (future: Notifications, Compensation/Analytics via IPerformanceReadService) react to this.
/// </summary>
public sealed record ReviewParticipantCompleted(Guid TenantId, Guid ReviewCycleId, Guid ParticipantEmployeeId, bool WasForced) : IntegrationEvent(TenantId);

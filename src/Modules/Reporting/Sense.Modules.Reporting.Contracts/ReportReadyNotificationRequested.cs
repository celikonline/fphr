using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Reporting.Contracts;

/// <summary>Durably queued after a report succeeds so transient mail failures retry independently of generation.</summary>
public sealed record ReportReadyNotificationRequested(
    Guid TenantId, Guid ExecutionId, Guid RequestedByUserId, string Email, string? DisplayName, string ReportKey, string CultureName)
    : IntegrationEvent(TenantId, RequestedByUserId);

using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Reporting.Contracts;

/// <summary>Server-owned requester snapshot, durably delivered only after the execution transaction commits.</summary>
public sealed record ReportGenerationRequested(
    Guid TenantId, Guid ExecutionId, Guid RequestedByUserId, Guid? EmployeeId, string? Email, string? DisplayName,
    string CultureName, bool Notify, IReadOnlyList<Guid>? VisibleEmployeeIds, bool CanViewSensitive)
    : IntegrationEvent(TenantId, RequestedByUserId);

/// <summary>Request-time scope ceiling for a background provider. Current permissions/scope are also revalidated.</summary>
public interface IReportGenerationContext
{
    bool IsBackground { get; }
    IReadOnlySet<Guid>? VisibleEmployeeIds { get; }
    bool CanViewSensitive { get; }
}

namespace Sense.Shared.Contracts.Modules;

/// <summary>Cross-module license capacity; null means this tenant is not managed by the sales portal.</summary>
public interface ICommercialEntitlements
{
    Task<int?> GetEmployeeLimitAsync(Guid tenantId, CancellationToken ct);
}

public sealed class CommercialCapacityExceededException(int limit) : Exception("commercial.employee_capacity_exceeded")
{
    public int Limit { get; } = limit;
}

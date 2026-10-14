namespace Sense.Modules.Employee.Contracts;

/// <summary>Employee-owned cost-center assignments for other modules, isolated by tenant.</summary>
public interface IEmployeeCostCenterLookup
{
    Task<IReadOnlyDictionary<Guid, Guid>> GetAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default);
}

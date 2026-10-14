using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Organization.Domain.CostCenters;

/// <summary>An optional employee-specific cost center. A missing row means inherit from the employee's org unit.</summary>
public sealed class EmployeeCostCenterOverride : TenantAggregateRoot<Guid>, IAuditLogged
{
    private EmployeeCostCenterOverride() { }

    private EmployeeCostCenterOverride(Guid id, Guid tenantId, Guid employeeId, Guid costCenterId) : base(id, tenantId)
    {
        EmployeeId = employeeId;
        CostCenterId = costCenterId;
    }

    public Guid EmployeeId { get; private set; }

    public Guid CostCenterId { get; private set; }

    public static EmployeeCostCenterOverride Create(Guid tenantId, Guid employeeId, Guid costCenterId) =>
        new(Guid.CreateVersion7(), tenantId, employeeId, costCenterId);

    public void ChangeCostCenter(Guid costCenterId) => CostCenterId = costCenterId;
}

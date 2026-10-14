using Sense.Modules.Employee.Contracts;
using Sense.Modules.Organization.Domain;
using Sense.Modules.Organization.Domain.CostCenters;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Organization.Application.CostCenters;

public sealed record EmployeeCostCenterAssignmentDto(
    Guid EmployeeId,
    Guid? OrgUnitCostCenterId,
    Guid? EmployeeOverrideCostCenterId,
    Guid? EffectiveCostCenterId,
    string Source,
    Guid? EmployeeDirectCostCenterId = null);

[RequiresPermission("organization.cost_centers.view")]
public sealed record GetEmployeeCostCenterAssignmentQuery(Guid EmployeeId) : IQuery<EmployeeCostCenterAssignmentDto>, IScopedRequest
{
    public Guid? TargetEmployeeId => EmployeeId;
}

public sealed class GetEmployeeCostCenterAssignmentHandler(
    ITenantContext tenant,
    IEmployeeLookup employees,
    IOrgUnitRepository units,
    IEmployeeCostCenterOverrideRepository overrides,
    IEmployeeCostCenterLookup? directAssignments = null) : IQueryHandler<GetEmployeeCostCenterAssignmentQuery, EmployeeCostCenterAssignmentDto>
{
    public async Task<Result<EmployeeCostCenterAssignmentDto>> Handle(GetEmployeeCostCenterAssignmentQuery query, CancellationToken cancellationToken)
    {
        if (!await employees.ExistsActiveAsync(tenant.TenantId, query.EmployeeId, cancellationToken))
            return Error.NotFound(OrganizationErrors.CostCenterEmployeeNotFound);
        return await EmployeeCostCenterAssignmentResolver.ResolveAsync(tenant.TenantId, query.EmployeeId, employees, units, overrides, cancellationToken, directAssignments);
    }
}

[RequiresPermission("organization.cost_centers.manage")]
public sealed record SetEmployeeCostCenterOverrideCommand(Guid EmployeeId, Guid? CostCenterId) : ICommand<EmployeeCostCenterAssignmentDto>, IScopedRequest
{
    public Guid? TargetEmployeeId => EmployeeId;
}

public sealed class SetEmployeeCostCenterOverrideHandler(
    ITenantContext tenant,
    IEmployeeLookup employees,
    ICostCenterRepository costCenters,
    IEmployeeCostCenterOverrideRepository overrides,
    IOrgUnitRepository units,
    IEmployeeCostCenterLookup? directAssignments = null) : ICommandHandler<SetEmployeeCostCenterOverrideCommand, EmployeeCostCenterAssignmentDto>
{
    public async Task<Result<EmployeeCostCenterAssignmentDto>> Handle(SetEmployeeCostCenterOverrideCommand command, CancellationToken cancellationToken)
    {
        if (!await employees.ExistsActiveAsync(tenant.TenantId, command.EmployeeId, cancellationToken))
            return Error.NotFound(OrganizationErrors.CostCenterEmployeeNotFound);

        if (command.CostCenterId is { } costCenterId)
        {
            var center = await costCenters.GetByIdAsync(costCenterId, cancellationToken);
            if (center.IsFailure || !center.Value.IsActive) return Error.NotFound(OrganizationDomainErrors.CostCenterNotFound);
        }

        var existing = await overrides.FindByEmployeeIdAsync(command.EmployeeId, cancellationToken);
        if (command.CostCenterId is { } assignedId)
        {
            if (existing is null) overrides.Add(EmployeeCostCenterOverride.Create(tenant.TenantId, command.EmployeeId, assignedId));
            else existing.ChangeCostCenter(assignedId);
        }
        else if (existing is not null)
        {
            overrides.Remove(existing);
        }

        return await EmployeeCostCenterAssignmentResolver.ResolveAsync(tenant.TenantId, command.EmployeeId, employees, units, overrides, cancellationToken, directAssignments);
    }
}

internal static class EmployeeCostCenterAssignmentResolver
{
    public static async Task<Result<EmployeeCostCenterAssignmentDto>> ResolveAsync(
        Guid tenantId,
        Guid employeeId,
        IEmployeeLookup employees,
        IOrgUnitRepository units,
        IEmployeeCostCenterOverrideRepository overrides,
        CancellationToken cancellationToken,
        IEmployeeCostCenterLookup? directAssignments = null)
    {
        var employeeOverride = await overrides.FindByEmployeeIdAsync(employeeId, cancellationToken);
        var orgUnitId = await employees.GetOrgUnitIdAsync(tenantId, employeeId, cancellationToken);
        Guid? inherited = null;
        var visited = new HashSet<Guid>();
        while (orgUnitId is { } current && visited.Add(current))
        {
            var result = await units.GetByIdAsync(current, cancellationToken);
            if (result.IsFailure) break;
            if (result.Value.CostCenterId is { } assigned)
            {
                inherited = assigned;
                break;
            }
            orgUnitId = result.Value.ParentId;
        }

        var direct = directAssignments is null ? null : await directAssignments.GetAsync(tenantId, [employeeId], cancellationToken);
        Guid? directId = direct is not null && direct.TryGetValue(employeeId, out var assignedDirect) ? assignedDirect : null;
        var overrideId = employeeOverride?.CostCenterId;
        return new EmployeeCostCenterAssignmentDto(
            employeeId,
            inherited,
            overrideId,
            directId ?? overrideId ?? inherited,
            directId.HasValue ? "employeeDirect" : overrideId.HasValue ? "employee" : inherited.HasValue ? "orgUnit" : "unassigned",
            directId);
    }
}

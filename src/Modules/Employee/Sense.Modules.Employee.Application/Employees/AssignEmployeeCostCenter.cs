using FluentValidation;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Employee.Application.Employees;

[RequiresPermission(EmployeePermissions.EmployeesUpdateKey)]
public sealed record AssignEmployeeCostCenterCommand(Guid EmployeeId, Guid? CostCenterId)
    : ICommand<EmployeeDetailDto>, IScopedRequest
{
    public Guid? TargetEmployeeId => EmployeeId;
}

public sealed class AssignEmployeeCostCenterValidator : AbstractValidator<AssignEmployeeCostCenterCommand>
{
    public AssignEmployeeCostCenterValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.CostCenterId).Must(id => id != Guid.Empty);
    }
}

public sealed class AssignEmployeeCostCenterHandler(
    ITenantContext tenant,
    IEmployeeRepository employees,
    ICostCenterLookup costCenters,
    EmployeeDetailBuilder details) : ICommandHandler<AssignEmployeeCostCenterCommand, EmployeeDetailDto>
{
    public async Task<Result<EmployeeDetailDto>> Handle(AssignEmployeeCostCenterCommand command, CancellationToken cancellationToken)
    {
        var found = await employees.GetByIdAsync(command.EmployeeId, cancellationToken);
        if (found.IsFailure) return found.Error;

        if (command.CostCenterId is { } costCenterId)
        {
            var center = await costCenters.FindAsync(tenant.TenantId, costCenterId, cancellationToken);
            if (center is null) return Error.NotFound(EmployeeErrors.CostCenterNotFound);
            // Existing assignments survive deactivation. Only a new assignment must be active.
            if (!center.IsActive && found.Value.CostCenterId != costCenterId)
                return Error.Rule(EmployeeErrors.CostCenterInactive);
        }

        found.Value.AssignCostCenter(command.CostCenterId);
        return await details.BuildAsync(found.Value, cancellationToken);
    }
}

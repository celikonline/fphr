using FluentValidation;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;
// Alias uses a name other than "Employee" - see Sense.Modules.Employee.Domain.Repositories.cs for why.
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Modules.Employee.Application.Employees;

/// <summary>Creates a minimal employee stub record.</summary>
[RequiresPermission("employee.employees.create")]
public sealed record CreateEmployeeCommand(string DisplayName, Guid? UserId, Guid? OrgUnitId) : ICommand<EmployeeListItemDto>;

public sealed class CreateEmployeeValidator : AbstractValidator<CreateEmployeeCommand>
{
    public CreateEmployeeValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(EmployeeLimits.DisplayNameMaxLength);
    }
}

public sealed class CreateEmployeeHandler(
    ITenantContext tenant,
    IEmployeeRepository employees,
    IOrgUnitLookup? orgUnitLookup = null) : ICommandHandler<CreateEmployeeCommand, EmployeeListItemDto>
{
    public async Task<Result<EmployeeListItemDto>> Handle(CreateEmployeeCommand command, CancellationToken cancellationToken)
    {
        if (command.OrgUnitId is { } orgUnitId && orgUnitLookup is not null &&
            !await orgUnitLookup.ExistsActiveAsync(tenant.TenantId, orgUnitId, cancellationToken))
        {
            return Error.NotFound(EmployeeErrors.OrgUnitNotFound);
        }

        var result = EmployeeEntity.Create(tenant.TenantId, command.DisplayName, command.UserId, command.OrgUnitId);
        if (result.IsFailure)
        {
            return result.Error;
        }

        employees.Add(result.Value);

        var e = result.Value;
        return new EmployeeListItemDto(e.Id, e.DisplayName, e.Status.ToString(), e.OrgUnitId, e.UserId);
    }
}

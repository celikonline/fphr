using FluentValidation;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;
using ContractEvents = Sense.Modules.Employee.Contracts.Events;

namespace Sense.Modules.Employee.Application.Employees;

/// <summary>
/// HR/admin write path for one employee's custom field values (11.3.03 "Özel alanlar"). Bulk-set semantics: only the
/// (DefinitionId, Value) pairs present in <see cref="Values"/> change, everything else stored is left untouched.
/// A sensitive definition's value is silently preserved (not overwritten) when the caller lacks employee.sensitive.view -
/// same precedent as <see cref="EmployeeSensitiveAccess.PreserveSensitiveFields"/> for TCKN.
/// </summary>
[RequiresPermission(EmployeePermissions.EmployeesUpdateKey)]
public sealed record SetEmployeeCustomFieldsCommand(Guid EmployeeId, IReadOnlyDictionary<Guid, string?> Values)
    : ICommand<IReadOnlyList<CustomFieldValueDto>>, IScopedRequest
{
    public Guid? TargetEmployeeId => EmployeeId;
}

public sealed class SetEmployeeCustomFieldsValidator : AbstractValidator<SetEmployeeCustomFieldsCommand>
{
    public SetEmployeeCustomFieldsValidator() => RuleFor(x => x.EmployeeId).NotEmpty();
}

public sealed class SetEmployeeCustomFieldsHandler(
    ITenantContext tenant,
    ICurrentUser currentUser,
    IEmployeeRepository employees,
    ICustomFieldDefinitionRepository definitions,
    EmployeeSensitiveAccess sensitiveAccess,
    IIntegrationEventOutbox outbox,
    CustomFieldValueReader reader) : ICommandHandler<SetEmployeeCustomFieldsCommand, IReadOnlyList<CustomFieldValueDto>>
{
    public async Task<Result<IReadOnlyList<CustomFieldValueDto>>> Handle(SetEmployeeCustomFieldsCommand command, CancellationToken cancellationToken)
    {
        var found = await employees.GetWithCustomFieldsAsync(command.EmployeeId, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var employee = found.Value;
        var defs = (await definitions.ListActiveAsync(cancellationToken)).ToDictionary(d => d.Id);
        var canViewSensitive = await sensitiveAccess.CanViewAsync(cancellationToken);

        var toApply = new Dictionary<Guid, string?>();
        foreach (var (definitionId, rawValue) in command.Values)
        {
            if (!defs.TryGetValue(definitionId, out var definition))
            {
                return Error.NotFound(EmployeeErrors.CustomFieldDefinitionNotFound);
            }

            // Without employee.sensitive.view the caller cannot set (or clear) what they cannot see: the stored value wins.
            if (definition.IsSensitive && !canViewSensitive)
            {
                continue;
            }

            var coerced = CustomFieldValueCoercion.Coerce(definition, rawValue);
            if (coerced.IsFailure)
            {
                return coerced.Error;
            }

            toApply[definitionId] = coerced.Value;
        }

        var changed = employee.SetCustomFieldValues(toApply);
        if (changed)
        {
            outbox.Enqueue(new ContractEvents.EmployeeProfileChanged(tenant.TenantId, employee.Id, ContractEvents.EmployeeProfileSections.CustomFields)
            {
                ActorUserId = currentUser.UserId,
            });
        }

        return await reader.BuildAsync(employee.Id, cancellationToken);
    }
}

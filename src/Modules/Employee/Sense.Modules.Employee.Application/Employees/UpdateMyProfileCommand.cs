using FluentValidation;
using Sense.Modules.Employee.Application.Payments;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Kernel.ValueObjects;
using ContractEvents = Sense.Modules.Employee.Contracts.Events;

namespace Sense.Modules.Employee.Application.Employees;

/// <summary>
/// Self-service edit ("Self-servis düzenleme", 11.3.03): the caller's own contact/address fields plus their own
/// employee-editable custom field values, in one form/one save. <see cref="CustomFieldValues"/> follows the same
/// bulk-set semantics as the HR path, but only definitions with IsEmployeeEditable may be touched.
/// Known gap (E4): EmployeeEditRequiresApproval fields are applied directly - approval routing needs the Workflow
/// module (PC-B) and is out of scope here.
/// </summary>
[RequiresPermission(EmployeePermissions.ProfileSelfEditKey)]
public sealed record UpdateMyProfileCommand(
    string? PersonalEmail,
    string? MobilePhone,
    string? HomePhone,
    string? AddressLine,
    string? City,
    string? PostalCode,
    IReadOnlyDictionary<Guid, string?>? CustomFieldValues) : ICommand<EmployeeDetailDto>;

public sealed class UpdateMyProfileValidator : AbstractValidator<UpdateMyProfileCommand>
{
    public UpdateMyProfileValidator()
    {
        RuleFor(x => x.PersonalEmail)
            .Must(e => string.IsNullOrWhiteSpace(e) || EmployeeEmailRules.IsValid(e.Trim()))
            .WithMessage(EmployeeDomainErrors.PersonalEmailInvalid);
        RuleFor(x => PhoneNormalizer.Normalize(x.MobilePhone)).MaximumLength(EmployeeLimits.PhoneMaxLength).WithMessage(EmployeeDomainErrors.MobilePhoneTooLong);
        RuleFor(x => PhoneNormalizer.Normalize(x.HomePhone)).MaximumLength(EmployeeLimits.PhoneMaxLength).WithMessage(EmployeeDomainErrors.HomePhoneTooLong);
        RuleFor(x => x.AddressLine).MaximumLength(EmployeeLimits.AddressLineMaxLength).WithMessage(EmployeeDomainErrors.AddressLineTooLong);
        RuleFor(x => x.City).MaximumLength(EmployeeLimits.CityMaxLength).WithMessage(EmployeeDomainErrors.CityTooLong);
        RuleFor(x => x.PostalCode).MaximumLength(EmployeeLimits.PostalCodeMaxLength).WithMessage(EmployeeDomainErrors.PostalCodeTooLong);
    }
}

public sealed class UpdateMyProfileHandler(
    ITenantContext tenant,
    ICurrentUser currentUser,
    SelfEmployeeResolver self,
    IEmployeeRepository employees,
    ICustomFieldDefinitionRepository definitions,
    EmployeeSensitiveAccess sensitiveAccess,
    IIntegrationEventOutbox outbox,
    CustomFieldValueReader customFields,
    EmployeeDetailBuilder details) : ICommandHandler<UpdateMyProfileCommand, EmployeeDetailDto>
{
    public async Task<Result<EmployeeDetailDto>> Handle(UpdateMyProfileCommand command, CancellationToken cancellationToken)
    {
        var employeeId = await self.ResolveAsync(cancellationToken);
        if (employeeId is null)
        {
            return Error.NotFound(EmployeeErrors.EmployeeNotFound);
        }

        var found = await employees.GetWithCustomFieldsAsync(employeeId.Value, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var employee = found.Value;
        var contact = new EmployeeContactInfo(command.PersonalEmail, command.MobilePhone, command.HomePhone, command.AddressLine, command.City, command.PostalCode);
        var updatedContact = employee.UpdateContactInfo(contact);
        if (updatedContact.IsFailure)
        {
            return updatedContact.Error;
        }

        var fieldsChanged = false;
        if (command.CustomFieldValues is { Count: > 0 } requested)
        {
            var defs = (await definitions.ListActiveAsync(cancellationToken)).ToDictionary(d => d.Id);
            var canViewSensitive = await sensitiveAccess.CanViewAsync(cancellationToken);
            var toApply = new Dictionary<Guid, string?>();
            foreach (var (definitionId, rawValue) in requested)
            {
                if (!defs.TryGetValue(definitionId, out var definition) || !definition.IsEmployeeEditable)
                {
                    return Error.Forbidden(EmployeeErrors.CustomFieldNotEmployeeEditable);
                }

                // A field that is both employee-editable and sensitive still needs employee.sensitive.view: the
                // employee cannot see (and therefore cannot knowingly set) a value they are not allowed to view,
                // same precedent as the HR path (SetEmployeeCustomFieldsHandler) and TCKN's PreserveSensitiveFields.
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

            fieldsChanged = employee.SetCustomFieldValues(toApply);
        }

        if (updatedContact.Value || fieldsChanged)
        {
            outbox.Enqueue(new ContractEvents.EmployeeProfileChanged(tenant.TenantId, employee.Id, ContractEvents.EmployeeProfileSections.SelfService)
            {
                ActorUserId = currentUser.UserId,
            });
        }

        var customFieldValues = await customFields.BuildFromAggregateAsync(employee, cancellationToken);
        return await details.BuildAsync(employee, customFieldValues, cancellationToken);
    }
}

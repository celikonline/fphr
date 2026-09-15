using FluentValidation;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.CustomFields;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Employee.Application.CustomFields;

/// <summary>Creates a custom field definition. Key and DataType are immutable once created (see CustomFieldDefinition).</summary>
[RequiresPermission(EmployeePermissions.CustomFieldManageKey)]
public sealed record CreateCustomFieldDefinitionCommand(
    string Key,
    string Name,
    CustomFieldDataType DataType,
    CustomFieldSection Section,
    IReadOnlyList<string>? Options,
    bool IsRequired,
    bool IsEmployeeEditable,
    bool EmployeeEditRequiresApproval,
    bool IsReportable = true,
    bool IsSensitive = false,
    int SortOrder = 0) : ICommand<CustomFieldDefinitionDto>
{
    public CustomFieldDefinitionData ToData() => new(Name, Section, Options, IsRequired, IsEmployeeEditable, EmployeeEditRequiresApproval, IsReportable, IsSensitive, SortOrder);
}

/// <summary>Updates a custom field definition (everything but Key/DataType).</summary>
[RequiresPermission(EmployeePermissions.CustomFieldManageKey)]
public sealed record UpdateCustomFieldDefinitionCommand(
    Guid Id,
    string Name,
    CustomFieldSection Section,
    IReadOnlyList<string>? Options,
    bool IsRequired,
    bool IsEmployeeEditable,
    bool EmployeeEditRequiresApproval,
    bool IsReportable,
    bool IsSensitive,
    int SortOrder) : ICommand<CustomFieldDefinitionDto>
{
    public CustomFieldDefinitionData ToData() => new(Name, Section, Options, IsRequired, IsEmployeeEditable, EmployeeEditRequiresApproval, IsReportable, IsSensitive, SortOrder);
}

[RequiresPermission(EmployeePermissions.CustomFieldManageKey)]
public sealed record DeactivateCustomFieldDefinitionCommand(Guid Id) : ICommand<CustomFieldDefinitionDto>;

[RequiresPermission(EmployeePermissions.CustomFieldManageKey)]
public sealed record ReactivateCustomFieldDefinitionCommand(Guid Id) : ICommand<CustomFieldDefinitionDto>;

public sealed class CreateCustomFieldDefinitionValidator : AbstractValidator<CreateCustomFieldDefinitionCommand>
{
    public CreateCustomFieldDefinitionValidator()
    {
        RuleFor(x => x.Key).NotEmpty().WithMessage(CustomFieldDomainErrors.KeyRequired)
            .MaximumLength(CustomFieldLimits.KeyMaxLength).WithMessage(CustomFieldDomainErrors.KeyTooLong);
        RuleFor(x => x.Name).NotEmpty().WithMessage(CustomFieldDomainErrors.NameRequired)
            .MaximumLength(CustomFieldLimits.NameMaxLength).WithMessage(CustomFieldDomainErrors.NameTooLong);
        RuleFor(x => x.DataType).IsInEnum().WithMessage(CustomFieldDomainErrors.DataTypeInvalid);
        RuleFor(x => x.Section).IsInEnum().WithMessage(CustomFieldDomainErrors.SectionInvalid);
    }
}

public sealed class UpdateCustomFieldDefinitionValidator : AbstractValidator<UpdateCustomFieldDefinitionCommand>
{
    public UpdateCustomFieldDefinitionValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().WithMessage(CustomFieldDomainErrors.NameRequired)
            .MaximumLength(CustomFieldLimits.NameMaxLength).WithMessage(CustomFieldDomainErrors.NameTooLong);
        RuleFor(x => x.Section).IsInEnum().WithMessage(CustomFieldDomainErrors.SectionInvalid);
    }
}

public sealed class CreateCustomFieldDefinitionHandler(
    ITenantContext tenant,
    ICustomFieldDefinitionRepository definitions) : ICommandHandler<CreateCustomFieldDefinitionCommand, CustomFieldDefinitionDto>
{
    public async Task<Result<CustomFieldDefinitionDto>> Handle(CreateCustomFieldDefinitionCommand command, CancellationToken cancellationToken)
    {
        var normalizedKey = command.Key.Trim().ToLowerInvariant();
        if (await definitions.KeyTakenAsync(normalizedKey, exceptId: null, cancellationToken))
        {
            return Error.Conflict(EmployeeErrors.CustomFieldKeyTaken);
        }

        var created = CustomFieldDefinition.Create(tenant.TenantId, command.Key, command.DataType, command.ToData());
        if (created.IsFailure)
        {
            return created.Error;
        }

        definitions.Add(created.Value);
        return CustomFieldDefinitionDto.From(created.Value);
    }
}

public sealed class UpdateCustomFieldDefinitionHandler(ICustomFieldDefinitionRepository definitions)
    : ICommandHandler<UpdateCustomFieldDefinitionCommand, CustomFieldDefinitionDto>
{
    public async Task<Result<CustomFieldDefinitionDto>> Handle(UpdateCustomFieldDefinitionCommand command, CancellationToken cancellationToken)
    {
        var found = await definitions.GetByIdAsync(command.Id, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var updated = found.Value.Update(command.ToData());
        return updated.IsFailure ? updated.Error : CustomFieldDefinitionDto.From(found.Value);
    }
}

public sealed class DeactivateCustomFieldDefinitionHandler(ICustomFieldDefinitionRepository definitions)
    : ICommandHandler<DeactivateCustomFieldDefinitionCommand, CustomFieldDefinitionDto>
{
    public async Task<Result<CustomFieldDefinitionDto>> Handle(DeactivateCustomFieldDefinitionCommand command, CancellationToken cancellationToken)
    {
        var found = await definitions.GetByIdAsync(command.Id, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var deactivated = found.Value.Deactivate();
        return deactivated.IsFailure ? deactivated.Error : CustomFieldDefinitionDto.From(found.Value);
    }
}

public sealed class ReactivateCustomFieldDefinitionHandler(ICustomFieldDefinitionRepository definitions)
    : ICommandHandler<ReactivateCustomFieldDefinitionCommand, CustomFieldDefinitionDto>
{
    public async Task<Result<CustomFieldDefinitionDto>> Handle(ReactivateCustomFieldDefinitionCommand command, CancellationToken cancellationToken)
    {
        var found = await definitions.GetByIdAsync(command.Id, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var reactivated = found.Value.Reactivate();
        return reactivated.IsFailure ? reactivated.Error : CustomFieldDefinitionDto.From(found.Value);
    }
}

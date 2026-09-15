using FluentValidation;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.DocumentTypes;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Documents.Application.DocumentTypes;

/// <summary>Creates a tenant-custom document type (IsSystem = false; system rows come only from the seed).</summary>
[RequiresPermission("documents.document_types.manage")]
public sealed record CreateDocumentTypeCommand(
    string Code,
    string Name,
    DocumentTypeStage Stage,
    bool IsRequired,
    DocumentRequirementCondition? Condition,
    int? RenewalPeriodDays,
    int Quantity,
    bool IsSensitive,
    int? DefaultRetentionDays,
    int SortOrder) : ICommand<DocumentTypeDto>;

public sealed class CreateDocumentTypeValidator : AbstractValidator<CreateDocumentTypeCommand>
{
    public CreateDocumentTypeValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(DocumentsLimits.DocumentTypeCodeMaxLength);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(DocumentsLimits.DocumentTypeNameMaxLength);
        RuleFor(x => x.Quantity).GreaterThanOrEqualTo(1);
        RuleFor(x => x.RenewalPeriodDays).GreaterThanOrEqualTo(1).When(x => x.RenewalPeriodDays is not null);
        RuleFor(x => x.DefaultRetentionDays).GreaterThanOrEqualTo(1).When(x => x.DefaultRetentionDays is not null);
    }
}

public sealed class CreateDocumentTypeHandler(ITenantContext tenant, IDocumentTypeDefinitionRepository types)
    : ICommandHandler<CreateDocumentTypeCommand, DocumentTypeDto>
{
    public async Task<Result<DocumentTypeDto>> Handle(CreateDocumentTypeCommand command, CancellationToken cancellationToken)
    {
        if (await types.CodeExistsAsync(command.Code, cancellationToken))
        {
            return Error.Conflict(DocumentsErrors.DocumentTypeCodeTaken);
        }

        var created = DocumentTypeDefinition.Create(
            tenant.TenantId, command.Code, command.Name, command.Stage, command.IsRequired, command.Condition,
            command.RenewalPeriodDays, command.Quantity, command.IsSensitive, command.DefaultRetentionDays,
            isSystem: false, command.SortOrder);
        if (created.IsFailure)
        {
            return created.Error;
        }

        types.Add(created.Value);
        return DocumentTypeDto.From(created.Value);
    }
}

/// <summary>Editable fields only (not Code/Stage/Condition/IsSystem).</summary>
[RequiresPermission("documents.document_types.manage")]
public sealed record UpdateDocumentTypeCommand(
    Guid Id,
    string Name,
    bool IsRequired,
    int? RenewalPeriodDays,
    int Quantity,
    bool IsSensitive,
    int? DefaultRetentionDays,
    int SortOrder) : ICommand<DocumentTypeDto>;

public sealed class UpdateDocumentTypeValidator : AbstractValidator<UpdateDocumentTypeCommand>
{
    public UpdateDocumentTypeValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(DocumentsLimits.DocumentTypeNameMaxLength);
        RuleFor(x => x.Quantity).GreaterThanOrEqualTo(1);
        RuleFor(x => x.RenewalPeriodDays).GreaterThanOrEqualTo(1).When(x => x.RenewalPeriodDays is not null);
        RuleFor(x => x.DefaultRetentionDays).GreaterThanOrEqualTo(1).When(x => x.DefaultRetentionDays is not null);
    }
}

public sealed class UpdateDocumentTypeHandler(IDocumentTypeDefinitionRepository types)
    : ICommandHandler<UpdateDocumentTypeCommand, DocumentTypeDto>
{
    public async Task<Result<DocumentTypeDto>> Handle(UpdateDocumentTypeCommand command, CancellationToken cancellationToken)
    {
        var type = await types.GetByIdAsync(command.Id, cancellationToken);
        if (type.IsFailure)
        {
            return type.Error;
        }

        var updated = type.Value.Update(
            command.Name, command.IsRequired, command.RenewalPeriodDays, command.Quantity, command.IsSensitive,
            command.DefaultRetentionDays, command.SortOrder);
        return updated.IsFailure ? updated.Error : DocumentTypeDto.From(type.Value);
    }
}

[RequiresPermission("documents.document_types.manage")]
public sealed record SetDocumentTypeActiveCommand(Guid Id, bool IsActive) : ICommand<DocumentTypeDto>;

public sealed class SetDocumentTypeActiveValidator : AbstractValidator<SetDocumentTypeActiveCommand>
{
    public SetDocumentTypeActiveValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class SetDocumentTypeActiveHandler(IDocumentTypeDefinitionRepository types)
    : ICommandHandler<SetDocumentTypeActiveCommand, DocumentTypeDto>
{
    public async Task<Result<DocumentTypeDto>> Handle(SetDocumentTypeActiveCommand command, CancellationToken cancellationToken)
    {
        var type = await types.GetByIdAsync(command.Id, cancellationToken);
        if (type.IsFailure)
        {
            return type.Error;
        }

        if (command.IsActive)
        {
            type.Value.Activate();
        }
        else
        {
            type.Value.Deactivate();
        }

        return DocumentTypeDto.From(type.Value);
    }
}

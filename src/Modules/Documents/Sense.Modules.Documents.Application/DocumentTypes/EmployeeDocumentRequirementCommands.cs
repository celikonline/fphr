using FluentValidation;
using Sense.Modules.Documents.Domain;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Documents.Application.DocumentTypes;

[RequiresPermission("documents.requirements.manage")]
public sealed record WaiveDocumentRequirementCommand(Guid RequirementId, string Reason) : ICommand<EmployeeDocumentRequirementDto>;

public sealed class WaiveDocumentRequirementValidator : AbstractValidator<WaiveDocumentRequirementCommand>
{
    public WaiveDocumentRequirementValidator()
    {
        RuleFor(x => x.RequirementId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(DocumentsLimits.WaivedReasonMaxLength);
    }
}

public sealed class WaiveDocumentRequirementHandler(
    ICurrentUser currentUser, IEmployeeDocumentRequirementRepository requirements, IDocumentTypeDefinitionRepository types)
    : ICommandHandler<WaiveDocumentRequirementCommand, EmployeeDocumentRequirementDto>
{
    public async Task<Result<EmployeeDocumentRequirementDto>> Handle(WaiveDocumentRequirementCommand command, CancellationToken cancellationToken)
    {
        var requirement = await requirements.GetByIdAsync(command.RequirementId, cancellationToken);
        if (requirement.IsFailure)
        {
            return requirement.Error;
        }

        var waived = requirement.Value.Waive(command.Reason, currentUser.UserId ?? Guid.Empty);
        if (waived.IsFailure)
        {
            return waived.Error;
        }

        var type = await types.GetByIdAsync(requirement.Value.DocumentTypeId, cancellationToken);
        return type.IsFailure ? type.Error : EmployeeDocumentRequirementDto.From(requirement.Value, type.Value);
    }
}

/// <summary>Admin correction: undoes a waive, back to Missing.</summary>
[RequiresPermission("documents.requirements.manage")]
public sealed record ClearWaiveCommand(Guid RequirementId) : ICommand<EmployeeDocumentRequirementDto>;

public sealed class ClearWaiveValidator : AbstractValidator<ClearWaiveCommand>
{
    public ClearWaiveValidator() => RuleFor(x => x.RequirementId).NotEmpty();
}

public sealed class ClearWaiveHandler(IEmployeeDocumentRequirementRepository requirements, IDocumentTypeDefinitionRepository types)
    : ICommandHandler<ClearWaiveCommand, EmployeeDocumentRequirementDto>
{
    public async Task<Result<EmployeeDocumentRequirementDto>> Handle(ClearWaiveCommand command, CancellationToken cancellationToken)
    {
        var requirement = await requirements.GetByIdAsync(command.RequirementId, cancellationToken);
        if (requirement.IsFailure)
        {
            return requirement.Error;
        }

        requirement.Value.ClearWaive();

        var type = await types.GetByIdAsync(requirement.Value.DocumentTypeId, cancellationToken);
        return type.IsFailure ? type.Error : EmployeeDocumentRequirementDto.From(requirement.Value, type.Value);
    }
}

/// <summary>Links an uploaded Document to a requirement, marking it satisfied.</summary>
[RequiresPermission("documents.requirements.manage")]
public sealed record LinkDocumentToRequirementCommand(Guid RequirementId, Guid DocumentId) : ICommand<EmployeeDocumentRequirementDto>;

public sealed class LinkDocumentToRequirementValidator : AbstractValidator<LinkDocumentToRequirementCommand>
{
    public LinkDocumentToRequirementValidator()
    {
        RuleFor(x => x.RequirementId).NotEmpty();
        RuleFor(x => x.DocumentId).NotEmpty();
    }
}

public sealed class LinkDocumentToRequirementHandler(
    IEmployeeDocumentRequirementRepository requirements, IDocumentTypeDefinitionRepository types, IDocumentRepository documents)
    : ICommandHandler<LinkDocumentToRequirementCommand, EmployeeDocumentRequirementDto>
{
    public async Task<Result<EmployeeDocumentRequirementDto>> Handle(LinkDocumentToRequirementCommand command, CancellationToken cancellationToken) =>
        await LinkCore.LinkAsync(requirements, types, documents, command.RequirementId, command.DocumentId, ownerEmployeeId: null, cancellationToken);
}

/// <summary>Self-service: links an uploaded Document to the caller's own requirement, marking it satisfied.</summary>
[RequiresPermission("documents.my.requirements")]
public sealed record LinkMyDocumentToRequirementCommand(Guid RequirementId, Guid DocumentId) : ICommand<EmployeeDocumentRequirementDto>;

public sealed class LinkMyDocumentToRequirementValidator : AbstractValidator<LinkMyDocumentToRequirementCommand>
{
    public LinkMyDocumentToRequirementValidator()
    {
        RuleFor(x => x.RequirementId).NotEmpty();
        RuleFor(x => x.DocumentId).NotEmpty();
    }
}

public sealed class LinkMyDocumentToRequirementHandler(
    ICurrentUser currentUser, IEmployeeDocumentRequirementRepository requirements, IDocumentTypeDefinitionRepository types, IDocumentRepository documents)
    : ICommandHandler<LinkMyDocumentToRequirementCommand, EmployeeDocumentRequirementDto>
{
    public async Task<Result<EmployeeDocumentRequirementDto>> Handle(LinkMyDocumentToRequirementCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(DocumentsErrors.NoEmployeeForUser);
        }

        return await LinkCore.LinkAsync(requirements, types, documents, command.RequirementId, command.DocumentId, employeeId, cancellationToken);
    }
}

/// <summary>Shared by the HR-manage and self-service link commands; <paramref name="ownerEmployeeId"/> null = no ownership check (HR path).</summary>
internal static class LinkCore
{
    public static async Task<Result<EmployeeDocumentRequirementDto>> LinkAsync(
        IEmployeeDocumentRequirementRepository requirements,
        IDocumentTypeDefinitionRepository types,
        IDocumentRepository documents,
        Guid requirementId,
        Guid documentId,
        Guid? ownerEmployeeId,
        CancellationToken cancellationToken)
    {
        var requirement = await requirements.GetByIdAsync(requirementId, cancellationToken);
        if (requirement.IsFailure)
        {
            return requirement.Error;
        }

        if (ownerEmployeeId is { } employeeId && requirement.Value.EmployeeId != employeeId)
        {
            return Error.Forbidden(DocumentsErrors.NotOwner);
        }

        var document = await documents.GetByIdAsync(documentId, cancellationToken);
        if (document.IsFailure)
        {
            return document.Error;
        }

        requirement.Value.MarkUploaded(documentId);

        var type = await types.GetByIdAsync(requirement.Value.DocumentTypeId, cancellationToken);
        return type.IsFailure ? type.Error : EmployeeDocumentRequirementDto.From(requirement.Value, type.Value);
    }
}

/// <summary>Re-runs document requirement materialization for an existing employee (e.g. a profile field changed that affects Conditional applicability). Reuses DocumentRequirementMaterializationService - the same logic EmployeeHired uses.</summary>
[RequiresPermission("documents.requirements.manage")]
public sealed record ReevaluateEmployeeDocumentRequirementsCommand(Guid EmployeeId) : ICommand<IReadOnlyList<EmployeeDocumentRequirementDto>>;

public sealed class ReevaluateEmployeeDocumentRequirementsValidator : AbstractValidator<ReevaluateEmployeeDocumentRequirementsCommand>
{
    public ReevaluateEmployeeDocumentRequirementsValidator() => RuleFor(x => x.EmployeeId).NotEmpty();
}

public sealed class ReevaluateEmployeeDocumentRequirementsHandler(
    ITenantContext tenant, DocumentRequirementMaterializationService materialization, IDocumentTypeDefinitionRepository types, TimeProvider clock)
    : ICommandHandler<ReevaluateEmployeeDocumentRequirementsCommand, IReadOnlyList<EmployeeDocumentRequirementDto>>
{
    public async Task<Result<IReadOnlyList<EmployeeDocumentRequirementDto>>> Handle(
        ReevaluateEmployeeDocumentRequirementsCommand command, CancellationToken cancellationToken)
    {
        // No cross-module HireDate lookup exists today - see DocumentRequirementMaterializationService.MaterializeAsync's
        // dueDateBasis remark. The manual path uses "today" as the grace-period basis instead of the real hire date.
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var created = await materialization.MaterializeAsync(tenant.TenantId, command.EmployeeId, today, cancellationToken);

        var dtos = new List<EmployeeDocumentRequirementDto>(created.Count);
        foreach (var requirement in created)
        {
            var type = await types.GetByIdAsync(requirement.DocumentTypeId, cancellationToken);
            if (type.IsSuccess)
            {
                dtos.Add(EmployeeDocumentRequirementDto.From(requirement, type.Value));
            }
        }

        return Result.Success<IReadOnlyList<EmployeeDocumentRequirementDto>>(dtos);
    }
}

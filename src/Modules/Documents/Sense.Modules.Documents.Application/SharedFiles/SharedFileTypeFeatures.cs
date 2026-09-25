using FluentValidation;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.SharedFiles;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Documents.Application.SharedFiles;

public sealed record SharedFileTypeDto(Guid Id, string Name, bool VisibleOnMobile, bool IsActive, int SortOrder)
{
    public static SharedFileTypeDto From(SharedFileType t) => new(t.Id, t.Name, t.VisibleOnMobile, t.IsActive, t.SortOrder);
}

/// <summary>Lists the tenant's shared file types (E8); anyone who can see shared files may read the catalog.</summary>
[RequiresPermission("documents.company.view")]
public sealed record ListSharedFileTypesQuery(bool? IsActive = null) : IQuery<IReadOnlyList<SharedFileTypeDto>>;

public sealed class ListSharedFileTypesHandler(ISharedFileTypeRepository types)
    : IQueryHandler<ListSharedFileTypesQuery, IReadOnlyList<SharedFileTypeDto>>
{
    public async Task<Result<IReadOnlyList<SharedFileTypeDto>>> Handle(ListSharedFileTypesQuery query, CancellationToken cancellationToken)
    {
        var all = await types.ListAsync(query.IsActive, cancellationToken);
        return all.Select(SharedFileTypeDto.From).ToList();
    }
}

[RequiresPermission("documents.sharedfile_types.manage")]
public sealed record CreateSharedFileTypeCommand(string Name, bool VisibleOnMobile, int SortOrder = 0) : ICommand<SharedFileTypeDto>;

public sealed class CreateSharedFileTypeValidator : AbstractValidator<CreateSharedFileTypeCommand>
{
    public CreateSharedFileTypeValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(SharedFileType.NameMaxLength);
}

public sealed class CreateSharedFileTypeHandler(ITenantContext tenant, ISharedFileTypeRepository types)
    : ICommandHandler<CreateSharedFileTypeCommand, SharedFileTypeDto>
{
    public async Task<Result<SharedFileTypeDto>> Handle(CreateSharedFileTypeCommand command, CancellationToken cancellationToken)
    {
        if (await types.NameExistsAsync(command.Name.Trim(), null, cancellationToken))
        {
            return Error.Conflict(SharedFilesDomainErrors.TypeNameTaken);
        }

        var created = SharedFileType.Create(tenant.TenantId, command.Name, command.VisibleOnMobile, command.SortOrder);
        if (created.IsFailure)
        {
            return created.Error;
        }

        types.Add(created.Value);
        return SharedFileTypeDto.From(created.Value);
    }
}

[RequiresPermission("documents.sharedfile_types.manage")]
public sealed record UpdateSharedFileTypeCommand(Guid TypeId, string Name, bool VisibleOnMobile, int SortOrder = 0) : ICommand<SharedFileTypeDto>;

public sealed class UpdateSharedFileTypeValidator : AbstractValidator<UpdateSharedFileTypeCommand>
{
    public UpdateSharedFileTypeValidator()
    {
        RuleFor(x => x.TypeId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(SharedFileType.NameMaxLength);
    }
}

public sealed class UpdateSharedFileTypeHandler(ISharedFileTypeRepository types)
    : ICommandHandler<UpdateSharedFileTypeCommand, SharedFileTypeDto>
{
    public async Task<Result<SharedFileTypeDto>> Handle(UpdateSharedFileTypeCommand command, CancellationToken cancellationToken)
    {
        var type = await types.GetByIdAsync(command.TypeId, cancellationToken);
        if (type.IsFailure)
        {
            return type.Error;
        }

        if (await types.NameExistsAsync(command.Name.Trim(), command.TypeId, cancellationToken))
        {
            return Error.Conflict(SharedFilesDomainErrors.TypeNameTaken);
        }

        var updated = type.Value.Update(command.Name, command.VisibleOnMobile, command.SortOrder);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        return SharedFileTypeDto.From(type.Value);
    }
}

[RequiresPermission("documents.sharedfile_types.manage")]
public sealed record SetSharedFileTypeActiveCommand(Guid TypeId, bool IsActive) : ICommand<SharedFileTypeDto>;

public sealed class SetSharedFileTypeActiveHandler(ISharedFileTypeRepository types)
    : ICommandHandler<SetSharedFileTypeActiveCommand, SharedFileTypeDto>
{
    public async Task<Result<SharedFileTypeDto>> Handle(SetSharedFileTypeActiveCommand command, CancellationToken cancellationToken)
    {
        var type = await types.GetByIdAsync(command.TypeId, cancellationToken);
        if (type.IsFailure)
        {
            return type.Error;
        }

        type.Value.SetActive(command.IsActive);
        return SharedFileTypeDto.From(type.Value);
    }
}

/// <summary>Re-classifies an existing shared file: type and "mobilde görünsün" (E8).</summary>
[RequiresPermission("documents.company.manage")]
public sealed record ClassifySharedFileCommand(Guid FileId, Guid? TypeId, bool VisibleOnMobile) : ICommand;

public sealed class ClassifySharedFileHandler(ISharedCompanyFileRepository files, ISharedFileTypeRepository types)
    : ICommandHandler<ClassifySharedFileCommand>
{
    public async Task<Result> Handle(ClassifySharedFileCommand command, CancellationToken cancellationToken)
    {
        var file = await files.GetByIdAsync(command.FileId, cancellationToken);
        if (file.IsFailure)
        {
            return file.Error;
        }

        if (command.TypeId is { } typeId)
        {
            var type = await types.GetByIdAsync(typeId, cancellationToken);
            if (type.IsFailure)
            {
                return type.Error;
            }
        }

        file.Value.Classify(command.TypeId, command.VisibleOnMobile);
        return Result.Success();
    }
}

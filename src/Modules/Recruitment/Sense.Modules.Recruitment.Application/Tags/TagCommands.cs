using FluentValidation;
using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Domain.Tags;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Recruitment.Application.Tags;

/// <summary>Creates a tag in the tenant's candidate tag catalog.</summary>
[RequiresPermission("recruitment.tags.manage")]
public sealed record CreateTagCommand(string Name) : ICommand<Guid>;

public sealed class CreateTagValidator : AbstractValidator<CreateTagCommand>
{
    public CreateTagValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(RecruitmentLimits.NameMaxLength);
}

public sealed class CreateTagHandler(ITenantContext tenant, ITagRepository tags) : ICommandHandler<CreateTagCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateTagCommand command, CancellationToken cancellationToken)
    {
        if (await tags.NameTakenAsync(tenant.TenantId, command.Name, cancellationToken))
        {
            return Error.Conflict(RecruitmentErrors.TagNameTaken);
        }

        var created = Tag.Create(tenant.TenantId, command.Name);
        if (created.IsFailure)
        {
            return created.Error;
        }

        tags.Add(created.Value);
        return created.Value.Id;
    }
}

/// <summary>Deactivates a tag; existing candidate assignments are kept (references it by id, not name).</summary>
[RequiresPermission("recruitment.tags.manage")]
public sealed record DeactivateTagCommand(Guid TagId) : ICommand;

public sealed class DeactivateTagHandler(ITagRepository tags) : ICommandHandler<DeactivateTagCommand>
{
    public async Task<Result> Handle(DeactivateTagCommand command, CancellationToken cancellationToken)
    {
        var existing = await tags.GetByIdAsync(command.TagId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var result = existing.Value.Deactivate();
        return result.IsFailure ? result.Error : Result.Success();
    }
}

/// <summary>Reactivates a previously deactivated tag.</summary>
[RequiresPermission("recruitment.tags.manage")]
public sealed record ReactivateTagCommand(Guid TagId) : ICommand;

public sealed class ReactivateTagHandler(ITagRepository tags) : ICommandHandler<ReactivateTagCommand>
{
    public async Task<Result> Handle(ReactivateTagCommand command, CancellationToken cancellationToken)
    {
        var existing = await tags.GetByIdAsync(command.TagId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        existing.Value.Reactivate();
        return Result.Success();
    }
}

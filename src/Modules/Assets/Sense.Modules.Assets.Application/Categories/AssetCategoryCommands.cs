using FluentValidation;
using Sense.Modules.Assets.Domain;
using Sense.Modules.Assets.Domain.Assets;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Assets.Application.Categories;

[RequiresPermission("assets.categories.manage")]
public sealed record CreateAssetCategoryCommand(string Code, string Name) : ICommand<Guid>;

public sealed class CreateAssetCategoryValidator : AbstractValidator<CreateAssetCategoryCommand>
{
    public CreateAssetCategoryValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(AssetsLimits.CategoryCodeMaxLength);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(AssetsLimits.CategoryNameMaxLength);
    }
}

public sealed class CreateAssetCategoryHandler(ITenantContext tenant, IAssetCategoryRepository categories)
    : ICommandHandler<CreateAssetCategoryCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateAssetCategoryCommand command, CancellationToken cancellationToken)
    {
        if (await categories.FindByCodeAsync(command.Code.Trim(), cancellationToken) is not null)
        {
            return Error.Conflict(AssetsDomainErrors.CategoryCodeTaken);
        }

        var created = AssetCategory.Create(tenant.TenantId, command.Code, command.Name);
        if (created.IsFailure)
        {
            return created.Error;
        }

        categories.Add(created.Value);
        return created.Value.Id;
    }
}

[RequiresPermission("assets.categories.manage")]
public sealed record UpdateAssetCategoryCommand(Guid CategoryId, string Name) : ICommand;

public sealed class UpdateAssetCategoryValidator : AbstractValidator<UpdateAssetCategoryCommand>
{
    public UpdateAssetCategoryValidator()
    {
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(AssetsLimits.CategoryNameMaxLength);
    }
}

public sealed class UpdateAssetCategoryHandler(IAssetCategoryRepository categories) : ICommandHandler<UpdateAssetCategoryCommand>
{
    public async Task<Result> Handle(UpdateAssetCategoryCommand command, CancellationToken cancellationToken)
    {
        var existing = await categories.GetByIdAsync(command.CategoryId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        return existing.Value.Rename(command.Name);
    }
}

using Sense.Modules.Assets.Domain;
using Sense.Modules.Assets.Domain.Assets;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Assets.Application.Categories;

/// <summary>Lists the tenant's asset categories, lazily seeding the default catalog on first call.</summary>
[RequiresPermission("assets.categories.view")]
public sealed record ListAssetCategoriesQuery : IQuery<IReadOnlyList<AssetCategoryDto>>;

public sealed class ListAssetCategoriesHandler(
    ITenantContext tenant, IAssetCategoryRepository categories, AssetCategoryProvisioningService provisioning, IAssetsUnitOfWork unitOfWork)
    : IQueryHandler<ListAssetCategoriesQuery, IReadOnlyList<AssetCategoryDto>>
{
    public async Task<Result<IReadOnlyList<AssetCategoryDto>>> Handle(ListAssetCategoriesQuery query, CancellationToken cancellationToken)
    {
        await provisioning.EnsureSeededAsync(tenant.TenantId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var list = await categories.ListAsync(cancellationToken);
        IReadOnlyList<AssetCategoryDto> dtos = list.Select(ToDto).ToList();
        return Result.Success(dtos);
    }

    internal static AssetCategoryDto ToDto(AssetCategory category) => new(category.Id, category.Code, category.Name, category.ReplacementIntervalDays);
}

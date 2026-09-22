using Sense.Modules.Assets.Domain;
using Sense.Modules.Assets.Domain.Assets;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Assets.Application.Assets;

/// <summary>Lists assets within the tenant, optionally filtered by employee/status/category (HR/manager view).</summary>
[RequiresPermission("assets.assets.view")]
public sealed record ListAssetsQuery(Guid? EmployeeId, AssetStatus? Status, Guid? CategoryId, PagedQuery Paging) : IQuery<PagedResult<AssetDto>>;

public sealed class ListAssetsHandler(IAssetRepository assets, IAssetCategoryRepository categories) : IQueryHandler<ListAssetsQuery, PagedResult<AssetDto>>
{
    public async Task<Result<PagedResult<AssetDto>>> Handle(ListAssetsQuery query, CancellationToken cancellationToken)
    {
        var total = await assets.CountAsync(query.EmployeeId, query.Status, query.CategoryId, cancellationToken);
        var list = await assets.ListAsync(query.EmployeeId, query.Status, query.CategoryId, query.Paging.Skip, query.Paging.PageSize, cancellationToken);

        var toDto = await AssetMapper.CreateAsync(categories, cancellationToken);
        var items = list.Select(toDto).ToList();
        return new PagedResult<AssetDto>(items, query.Paging.Page, query.Paging.PageSize, total);
    }
}

[RequiresPermission("assets.assets.view")]
public sealed record GetAssetQuery(Guid AssetId) : IQuery<AssetDto>;

public sealed class GetAssetHandler(IAssetRepository assets, IAssetCategoryRepository categories) : IQueryHandler<GetAssetQuery, AssetDto>
{
    public async Task<Result<AssetDto>> Handle(GetAssetQuery query, CancellationToken cancellationToken)
    {
        var existing = await assets.GetByIdAsync(query.AssetId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var category = await categories.GetByIdAsync(existing.Value.CategoryId, cancellationToken);
        return AssetMapper.ToDto(existing.Value, category.IsSuccess ? category.Value : null);
    }
}

/// <summary>The employee card's "Zimmet" tab (HR/manager view) - the assets currently assigned to this employee (no assignment history/ledger, see IAssetRepository.ListByEmployeeAsync remarks).</summary>
[RequiresPermission("assets.assets.view")]
public sealed record GetEmployeeAssetsQuery(Guid EmployeeId) : IQuery<IReadOnlyList<AssetDto>>;

public sealed class GetEmployeeAssetsHandler(IAssetRepository assets, IAssetCategoryRepository categories) : IQueryHandler<GetEmployeeAssetsQuery, IReadOnlyList<AssetDto>>
{
    public async Task<Result<IReadOnlyList<AssetDto>>> Handle(GetEmployeeAssetsQuery query, CancellationToken cancellationToken)
    {
        var list = await assets.ListByEmployeeAsync(query.EmployeeId, cancellationToken);
        var toDto = await AssetMapper.CreateAsync(categories, cancellationToken);
        IReadOnlyList<AssetDto> dtos = list.Select(toDto).ToList();
        return Result.Success(dtos);
    }
}

/// <summary>Self-service: the equipment currently assigned to the caller's own employee record.</summary>
[RequiresPermission("assets.my.assets")]
public sealed record GetMyAssetsQuery : IQuery<IReadOnlyList<AssetDto>>;

public sealed class GetMyAssetsHandler(ICurrentUser currentUser, IAssetRepository assets, IAssetCategoryRepository categories)
    : IQueryHandler<GetMyAssetsQuery, IReadOnlyList<AssetDto>>
{
    public async Task<Result<IReadOnlyList<AssetDto>>> Handle(GetMyAssetsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(AssetsErrors.NoEmployeeForUser);
        }

        var list = await assets.ListByEmployeeAsync(employeeId, cancellationToken);
        var toDto = await AssetMapper.CreateAsync(categories, cancellationToken);
        IReadOnlyList<AssetDto> dtos = list.Select(toDto).ToList();
        return Result.Success(dtos);
    }
}

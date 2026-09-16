using Sense.Modules.Assets.Domain.Assets;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Assets.Domain;

/// <summary>AssetCategory repository.</summary>
public interface IAssetCategoryRepository
{
    void Add(AssetCategory category);

    Task<Result<AssetCategory>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<AssetCategory?> FindByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AssetCategory>> ListAsync(CancellationToken cancellationToken = default);

    Task<bool> AnyAsync(CancellationToken cancellationToken = default);
}

/// <summary>Asset aggregate repository.</summary>
public interface IAssetRepository
{
    void Add(Asset asset);

    Task<Result<Asset>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Asset?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Asset>> ListAsync(
        Guid? employeeId, AssetStatus? status, Guid? categoryId, int skip, int take, CancellationToken cancellationToken = default);

    Task<int> CountAsync(Guid? employeeId, AssetStatus? status, Guid? categoryId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Asset>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);

    Task<bool> HasOpenAssetsAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>Count of the employee's currently Assigned (unreturned) assets - used to warn HR at offboarding time.</summary>
    Task<int> CountOpenAssetsAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default);
}

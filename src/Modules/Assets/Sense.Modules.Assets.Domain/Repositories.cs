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

/// <summary>An assignment ledger row together with the asset it belongs to (the employee-side history).</summary>
public sealed record AssetAssignmentHistoryItem(AssetAssignment Assignment, Asset Asset);

/// <summary>Lightweight per-asset projection for the summary board (no entity materialization).</summary>
public sealed record AssetSummaryRow(Guid CategoryId, AssetStatus Status, DateOnly? IssuedAt, DateOnly? WarrantyEndsOn, bool Acknowledged);

/// <summary>
/// Asset aggregate repository. Every asset returned here (Get/Find/List/ListByEmployee/FindByCategoryAndSerialNumber)
/// has its OPEN assignment ledger row loaded and nothing else (filtered include, no N+1) - the closed history is read
/// through <see cref="ListAssignmentsAsync"/>/<see cref="ListEmployeeHistoryAsync"/>.
/// </summary>
public interface IAssetRepository
{
    void Add(Asset asset);

    Task<Result<Asset>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Asset?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <param name="search">Case-insensitive "contains" over name/serial number/asset tag/brand/model; blank = no filter.</param>
    Task<IReadOnlyList<Asset>> ListAsync(
        Guid? employeeId, AssetStatus? status, Guid? categoryId, string? search, int skip, int take, CancellationToken cancellationToken = default);

    Task<int> CountAsync(Guid? employeeId, AssetStatus? status, Guid? categoryId, string? search, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Asset>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>True when another asset (not <paramref name="excludeAssetId"/>) already carries this asset tag, compared case-insensitively.</summary>
    Task<bool> IsAssetTagTakenAsync(string assetTag, Guid? excludeAssetId, CancellationToken cancellationToken = default);

    /// <summary>The asset's full assignment history, newest first.</summary>
    Task<IReadOnlyList<AssetAssignment>> ListAssignmentsAsync(Guid assetId, CancellationToken cancellationToken = default);

    /// <summary>Every assignment (open and closed) the employee ever had, newest first, with its asset.</summary>
    Task<IReadOnlyList<AssetAssignmentHistoryItem>> ListEmployeeHistoryAsync(Guid employeeId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AssetSummaryRow>> ListSummaryRowsAsync(CancellationToken cancellationToken = default);

    /// <summary>True when another asset (not <paramref name="excludeAssetId"/>) with the same serial number is currently Assigned - a physical item cannot be issued twice.</summary>
    Task<bool> IsSerialNumberAssignedElsewhereAsync(string serialNumber, Guid excludeAssetId, CancellationToken cancellationToken = default);

    /// <summary>The existing asset in this category with this serial number, if any (import row matching - 11.3.08 md.67 "aynı kategori + seri no ile mevcut varlık kullanılır").</summary>
    Task<Asset?> FindByCategoryAndSerialNumberAsync(Guid categoryId, string serialNumber, CancellationToken cancellationToken = default);

    /// <summary>True when an AssetUnreturnedAtTermination audit event for this asset+employee already exists in the module outbox (idempotency guard).</summary>
    Task<bool> HasUnreturnedTerminationAuditAsync(Guid tenantId, Guid assetId, Guid employeeId, CancellationToken cancellationToken = default);

    Task<bool> HasOpenAssetsAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>Count of the employee's currently Assigned (unreturned) assets - used to warn HR at offboarding time.</summary>
    Task<int> CountOpenAssetsAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default);
}

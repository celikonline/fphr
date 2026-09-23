using Microsoft.EntityFrameworkCore;
using Sense.Modules.Assets.Contracts.Events;
using Sense.Modules.Assets.Domain;
using Sense.Modules.Assets.Domain.Assets;
using Sense.Shared.Infrastructure.Persistence.Outbox;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Assets.Infrastructure.Persistence;

public sealed class AssetCategoryRepository(AssetsDbContext context) : IAssetCategoryRepository
{
    public void Add(AssetCategory category) => context.AssetCategories.Add(category);

    public async Task<Result<AssetCategory>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var category = await context.AssetCategories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        return category is null ? Error.NotFound(AssetsDomainErrors.CategoryNotFound) : category;
    }

    public async Task<AssetCategory?> FindByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        await context.AssetCategories.FirstOrDefaultAsync(c => c.Code == code, cancellationToken);

    public async Task<IReadOnlyList<AssetCategory>> ListAsync(CancellationToken cancellationToken = default) =>
        await context.AssetCategories.OrderBy(c => c.Name).ToListAsync(cancellationToken);

    public async Task<bool> AnyAsync(CancellationToken cancellationToken = default) =>
        await context.AssetCategories.AnyAsync(cancellationToken);
}

public sealed class AssetRepository(AssetsDbContext context) : IAssetRepository
{
    public void Add(Asset asset) => context.Assets.Add(asset);

    public async Task<Result<Asset>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var asset = await context.Assets.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        return asset is null ? Error.NotFound(AssetsDomainErrors.NotFound) : asset;
    }

    public async Task<Asset?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.Assets.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Asset>> ListAsync(
        Guid? employeeId, AssetStatus? status, Guid? categoryId, int skip, int take, CancellationToken cancellationToken = default) =>
        await Filter(context.Assets.AsQueryable(), employeeId, status, categoryId)
            .OrderByDescending(a => a.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<int> CountAsync(Guid? employeeId, AssetStatus? status, Guid? categoryId, CancellationToken cancellationToken = default) =>
        await Filter(context.Assets.AsQueryable(), employeeId, status, categoryId).CountAsync(cancellationToken);

    /// <summary>
    /// Assets currently assigned to this employee. No assignment history/ledger exists (Asset.Return clears
    /// AssignedEmployeeId, see Asset.cs remarks), so a returned asset can no longer be attributed to the employee
    /// who held it - this intentionally only returns the employee's *current* open assignments.
    /// </summary>
    public async Task<IReadOnlyList<Asset>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        await context.Assets
            .Where(a => a.AssignedEmployeeId == employeeId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<bool> HasOpenAssetsAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default) =>
        await context.Assets.AnyAsync(
            a => a.TenantId == tenantId && a.AssignedEmployeeId == employeeId && a.Status == AssetStatus.Assigned,
            cancellationToken);

    public async Task<bool> IsSerialNumberAssignedElsewhereAsync(string serialNumber, Guid excludeAssetId, CancellationToken cancellationToken = default) =>
        await context.Assets.AnyAsync(
            a => a.Id != excludeAssetId && a.Status == AssetStatus.Assigned && a.SerialNumber != null && a.SerialNumber.ToLower() == serialNumber.ToLower(),
            cancellationToken);

    public async Task<Asset?> FindByCategoryAndSerialNumberAsync(Guid categoryId, string serialNumber, CancellationToken cancellationToken = default) =>
        await context.Assets.FirstOrDefaultAsync(
            a => a.CategoryId == categoryId && a.SerialNumber != null && a.SerialNumber.ToLower() == serialNumber.ToLower(),
            cancellationToken);

    public async Task<bool> HasUnreturnedTerminationAuditAsync(Guid tenantId, Guid assetId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        var typeName = EventTypeRegistry.NameOf(typeof(AssetUnreturnedAtTermination));
        var containing = "{\"assetId\":\"" + assetId + "\",\"employeeId\":\"" + employeeId + "\"}";
        return await context.OutboxMessages.AsNoTracking()
            .AnyAsync(m => m.TenantId == tenantId && m.Type == typeName && EF.Functions.JsonContains(m.Payload, containing), cancellationToken);
    }

    public async Task<int> CountOpenAssetsAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default) =>
        await context.Assets.CountAsync(
            a => a.TenantId == tenantId && a.AssignedEmployeeId == employeeId && a.Status == AssetStatus.Assigned,
            cancellationToken);

    private static IQueryable<Asset> Filter(IQueryable<Asset> query, Guid? employeeId, AssetStatus? status, Guid? categoryId)
    {
        if (employeeId is { } emp)
        {
            query = query.Where(a => a.AssignedEmployeeId == emp);
        }

        if (status is { } s)
        {
            query = query.Where(a => a.Status == s);
        }

        if (categoryId is { } cat)
        {
            query = query.Where(a => a.CategoryId == cat);
        }

        return query;
    }
}

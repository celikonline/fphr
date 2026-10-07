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
    private const char LikeEscape = '\\';

    public void Add(Asset asset) => context.Assets.Add(asset);

    /// <summary>Assets with only their open (unreturned) ledger row loaded - the history is read through the dedicated history queries.</summary>
    private IQueryable<Asset> WithOpenAssignment() =>
        context.Assets.Include(a => a.Assignments.Where(x => x.ReturnedAt == null));

    public async Task<Result<Asset>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var asset = await WithOpenAssignment().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        return asset is null ? Error.NotFound(AssetsDomainErrors.NotFound) : asset;
    }

    public async Task<Asset?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await WithOpenAssignment().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Asset>> ListAsync(
        Guid? employeeId, AssetStatus? status, Guid? categoryId, string? search, int skip, int take, CancellationToken cancellationToken = default) =>
        await Filter(WithOpenAssignment(), employeeId, status, categoryId, search)
            .OrderByDescending(a => a.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<int> CountAsync(
        Guid? employeeId, AssetStatus? status, Guid? categoryId, string? search, CancellationToken cancellationToken = default) =>
        await Filter(context.Assets.AsQueryable(), employeeId, status, categoryId, search).CountAsync(cancellationToken);

    /// <summary>Assets currently assigned to this employee (open assignments only); the employee's past assignments come from <see cref="ListEmployeeHistoryAsync"/>.</summary>
    public async Task<IReadOnlyList<Asset>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        await WithOpenAssignment()
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
        await WithOpenAssignment().FirstOrDefaultAsync(
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

    public async Task<bool> IsAssetTagTakenAsync(string assetTag, Guid? excludeAssetId, CancellationToken cancellationToken = default)
    {
        var tag = assetTag.Trim().ToLower();
        return await context.Assets.AnyAsync(
            a => a.AssetTag != null && a.AssetTag.ToLower() == tag && (excludeAssetId == null || a.Id != excludeAssetId),
            cancellationToken);
    }

    public async Task<IReadOnlyList<AssetAssignment>> ListAssignmentsAsync(Guid assetId, CancellationToken cancellationToken = default) =>
        await context.AssetAssignments.AsNoTracking()
            .Where(a => a.AssetId == assetId)
            .OrderByDescending(a => a.IssuedAt).ThenByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AssetAssignmentHistoryItem>> ListEmployeeHistoryAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        var rows = await (
                from assignment in context.AssetAssignments.AsNoTracking()
                join asset in context.Assets.AsNoTracking() on assignment.AssetId equals asset.Id
                where assignment.EmployeeId == employeeId
                orderby assignment.IssuedAt descending, assignment.CreatedAt descending
                select new { assignment, asset })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new AssetAssignmentHistoryItem(r.assignment, r.asset)).ToList();
    }

    public async Task<IReadOnlyList<AssetSummaryRow>> ListSummaryRowsAsync(CancellationToken cancellationToken = default) =>
        await context.Assets.AsNoTracking()
            .Select(a => new AssetSummaryRow(
                a.CategoryId,
                a.Status,
                a.IssuedAt,
                a.WarrantyEndsOn,
                a.Assignments.Any(x => x.ReturnedAt == null && x.AcknowledgedAt != null)))
            .ToListAsync(cancellationToken);

    private static IQueryable<Asset> Filter(IQueryable<Asset> query, Guid? employeeId, AssetStatus? status, Guid? categoryId, string? search)
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

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = ContainsPattern(search);
            query = query.Where(a =>
                EF.Functions.ILike(a.Name, pattern, LikeEscape.ToString()) ||
                (a.SerialNumber != null && EF.Functions.ILike(a.SerialNumber, pattern, LikeEscape.ToString())) ||
                (a.AssetTag != null && EF.Functions.ILike(a.AssetTag, pattern, LikeEscape.ToString())) ||
                (a.Brand != null && EF.Functions.ILike(a.Brand, pattern, LikeEscape.ToString())) ||
                (a.Model != null && EF.Functions.ILike(a.Model, pattern, LikeEscape.ToString())));
        }

        return query;
    }

    private static string ContainsPattern(string term)
    {
        var escaped = term.Trim()
            .Replace(LikeEscape.ToString(), string.Concat(LikeEscape, LikeEscape), StringComparison.Ordinal)
            .Replace("%", string.Concat(LikeEscape, "%"), StringComparison.Ordinal)
            .Replace("_", string.Concat(LikeEscape, "_"), StringComparison.Ordinal);
        return string.Concat("%", escaped, "%");
    }
}

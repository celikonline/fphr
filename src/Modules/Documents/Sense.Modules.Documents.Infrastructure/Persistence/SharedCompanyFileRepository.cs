using Microsoft.EntityFrameworkCore;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.SharedFiles;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Documents.Infrastructure.Persistence;

/// <summary>Shared company file repository implementation with tenant isolation (senseik-028).</summary>
public sealed class SharedCompanyFileRepository(DocumentsDbContext context) : ISharedCompanyFileRepository
{
    public void Add(SharedCompanyFile file)
    {
        context.SharedCompanyFiles.Add(file);
    }

    public async Task<Result<SharedCompanyFile>> GetByIdAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        // Query is automatically filtered by tenant via global filter in DbContext
        var file = await context.SharedCompanyFiles
            .FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken);

        if (file is null)
        {
            return Error.NotFound(SharedFilesDomainErrors.NotFound);
        }

        return file;
    }

    public async Task<IReadOnlyList<SharedCompanyFile>> ListByTenantAsync(
        int skip = 0,
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        var files = await context.SharedCompanyFiles
            .Where(f => f.Status != SharedFileStatus.Deleted)
            .OrderByDescending(f => f.UploadedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return files.AsReadOnly();
    }

    public async Task<int> CountByTenantAsync(CancellationToken cancellationToken = default) =>
        await context.SharedCompanyFiles
            .Where(f => f.Status != SharedFileStatus.Deleted)
            .CountAsync(cancellationToken);

    public async Task<long> SumActiveSizeByTenantAsync(CancellationToken cancellationToken = default) =>
        await context.SharedCompanyFiles
            .Where(f => f.Status == SharedFileStatus.Active)
            .SumAsync(f => (long?)f.Size, cancellationToken) ?? 0L;
}

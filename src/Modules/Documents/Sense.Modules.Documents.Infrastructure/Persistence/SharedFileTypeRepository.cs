using Microsoft.EntityFrameworkCore;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.SharedFiles;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Documents.Infrastructure.Persistence;

/// <summary>Shared file type catalog repository (tenant-filtered by the DbContext global filter).</summary>
public sealed class SharedFileTypeRepository(DocumentsDbContext context) : ISharedFileTypeRepository
{
    public void Add(SharedFileType type) => context.SharedFileTypes.Add(type);

    public async Task<Result<SharedFileType>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var type = await context.SharedFileTypes.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        return type is null ? Error.NotFound(SharedFilesDomainErrors.TypeNotFound) : type;
    }

    public async Task<IReadOnlyList<SharedFileType>> ListAsync(bool? isActive, CancellationToken cancellationToken = default) =>
        await context.SharedFileTypes
            .Where(t => isActive == null || t.IsActive == isActive)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);

    public async Task<bool> NameExistsAsync(string name, Guid? excludeId, CancellationToken cancellationToken = default)
    {
        var lowered = name.ToLower();
        return await context.SharedFileTypes
            .AnyAsync(t => t.Name.ToLower() == lowered && (excludeId == null || t.Id != excludeId), cancellationToken);
    }
}

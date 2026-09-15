using Microsoft.EntityFrameworkCore;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.DocumentTypes;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Documents.Infrastructure.Persistence;

/// <summary>Document type catalog repository implementation with tenant isolation (via DbContext global filter).</summary>
public sealed class DocumentTypeDefinitionRepository(DocumentsDbContext context) : IDocumentTypeDefinitionRepository
{
    public void Add(DocumentTypeDefinition definition) => context.DocumentTypeDefinitions.Add(definition);

    public async Task<Result<DocumentTypeDefinition>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var definition = await context.DocumentTypeDefinitions.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        return definition is null ? Error.NotFound(DocumentsDomainErrors.DocumentTypeNotFound) : definition;
    }

    public async Task<IReadOnlyList<DocumentTypeDefinition>> ListAsync(DocumentTypeStage? stage, bool? isActive, CancellationToken cancellationToken = default)
    {
        var query = context.DocumentTypeDefinitions.AsQueryable();
        if (stage is { } s)
        {
            query = query.Where(d => d.Stage == s);
        }

        if (isActive is { } active)
        {
            query = query.Where(d => d.IsActive == active);
        }

        var list = await query.OrderBy(d => d.Stage).ThenBy(d => d.SortOrder).ToListAsync(cancellationToken);
        return list.AsReadOnly();
    }

    public async Task<bool> AnyForTenantAsync(CancellationToken cancellationToken = default) =>
        await context.DocumentTypeDefinitions.AnyAsync(cancellationToken);

    public async Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default) =>
        await context.DocumentTypeDefinitions.AnyAsync(d => d.Code == code, cancellationToken);
}

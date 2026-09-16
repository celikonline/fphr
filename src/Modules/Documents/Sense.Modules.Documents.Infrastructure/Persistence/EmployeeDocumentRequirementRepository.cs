using Microsoft.EntityFrameworkCore;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.DocumentTypes;
using Sense.Modules.Documents.Domain.Documents;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Documents.Infrastructure.Persistence;

/// <summary>Per-employee document requirement repository implementation with tenant isolation (via DbContext global filter).</summary>
public sealed class EmployeeDocumentRequirementRepository(DocumentsDbContext context) : IEmployeeDocumentRequirementRepository
{
    public void Add(EmployeeDocumentRequirement requirement) => context.EmployeeDocumentRequirements.Add(requirement);

    public async Task<Result<EmployeeDocumentRequirement>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var requirement = await context.EmployeeDocumentRequirements.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        return requirement is null ? Error.NotFound(DocumentsDomainErrors.EmployeeDocumentRequirementNotFound) : requirement;
    }

    public async Task<EmployeeDocumentRequirement?> FindAsync(Guid employeeId, Guid documentTypeId, CancellationToken cancellationToken = default) =>
        await context.EmployeeDocumentRequirements.FirstOrDefaultAsync(
            r => r.EmployeeId == employeeId && r.DocumentTypeId == documentTypeId, cancellationToken);

    public async Task<IReadOnlyList<EmployeeDocumentRequirement>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        var list = await context.EmployeeDocumentRequirements.Where(r => r.EmployeeId == employeeId).ToListAsync(cancellationToken);
        return list.AsReadOnly();
    }

    public async Task<(IReadOnlyList<EmployeeDocumentRequirement> Items, int Total)> ListReportAsync(
        DocumentRequirementStatus? status, int skip, int take, CancellationToken cancellationToken = default)
    {
        var query = context.EmployeeDocumentRequirements.AsQueryable();
        if (status is { } s)
        {
            query = query.Where(r => r.Status == s);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(r => r.DueDate).ThenBy(r => r.Id).Skip(skip).Take(take).ToListAsync(cancellationToken);
        return (items.AsReadOnly(), total);
    }

    public async Task<(IReadOnlyList<ExpiringDocumentRow> Items, int Total)> ListExpiringAsync(
        DateOnly today, int withinDays, DocumentExpiryState? state, int skip, int take, CancellationToken cancellationToken = default)
    {
        var threshold = today.AddDays(withinDays);

        // r.DocumentId is Guid? (nullable FK), Document.Id is Guid: cast the non-nullable side so the join key
        // types match. Only Uploaded rows are considered (see Where below), and MarkUploaded always sets
        // DocumentId, so this never joins against a null key in practice.
        var joined =
            from r in context.EmployeeDocumentRequirements
            join d in context.Documents on r.DocumentId equals (Guid?)d.Id
            where r.Status == DocumentRequirementStatus.Uploaded && d.ExpiryDate != null
            select new { r.Id, r.DocumentId, r.EmployeeId, r.DocumentTypeId, ExpiryDate = d.ExpiryDate!.Value };

        joined = state switch
        {
            DocumentExpiryState.Expired => joined.Where(x => x.ExpiryDate < today),
            DocumentExpiryState.Expiring => joined.Where(x => x.ExpiryDate >= today && x.ExpiryDate <= threshold),
            _ => joined.Where(x => x.ExpiryDate <= threshold),
        };

        var total = await joined.CountAsync(cancellationToken);
        var items = await joined
            .OrderBy(x => x.ExpiryDate)
            .Skip(skip)
            .Take(take)
            .Select(x => new ExpiringDocumentRow(x.Id, x.DocumentId!.Value, x.EmployeeId, x.DocumentTypeId, x.ExpiryDate))
            .ToListAsync(cancellationToken);

        return (items.AsReadOnly(), total);
    }
}

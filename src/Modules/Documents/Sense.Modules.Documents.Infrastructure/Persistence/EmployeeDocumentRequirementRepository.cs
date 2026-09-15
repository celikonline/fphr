using Microsoft.EntityFrameworkCore;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.DocumentTypes;
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
}

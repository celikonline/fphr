using Microsoft.EntityFrameworkCore;
using Sense.Modules.Documents.Application.VisaDocuments;
using Sense.Modules.Documents.Domain.VisaDocuments;
using Sense.Shared.Contracts.Context;

namespace Sense.Modules.Documents.Infrastructure.Persistence.Visa;

/// <summary>EF Core implementation of <see cref="IVisaDocumentRequestRepository"/>.</summary>
public sealed class VisaDocumentRequestRepository(DocumentsDbContext dbContext, ITenantContext tenantContext)
    : IVisaDocumentRequestRepository
{
    // Tracked (not AsNoTracking): callers mutate the aggregate and then call SaveAsync.
    public async Task<VisaDocumentRequest?> GetByIdAsync(Guid requestId, CancellationToken cancellationToken) =>
        await dbContext.VisaDocumentRequests
            .FirstOrDefaultAsync(x => x.Id == requestId && x.TenantId == tenantContext.TenantId, cancellationToken);

    public async Task<IReadOnlyList<VisaDocumentRequest>> GetByEmployeeIdAsync(
        Guid tenantId,
        Guid employeeId,
        VisaDocumentRequestStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.VisaDocumentRequests
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.EmployeeId == employeeId);

        if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        return await query.OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(VisaDocumentRequest request, CancellationToken cancellationToken) =>
        await dbContext.VisaDocumentRequests.AddAsync(request, cancellationToken);

    public async Task SaveAsync(CancellationToken cancellationToken) =>
        await dbContext.SaveChangesAsync(cancellationToken);
}

/// <summary>EF Core implementation of <see cref="IVisaDocumentProcessRepository"/>.</summary>
public sealed class VisaDocumentProcessRepository(DocumentsDbContext dbContext, ITenantContext tenantContext)
    : IVisaDocumentProcessRepository
{
    public async Task<VisaDocumentProcess?> GetByIdAsync(Guid processId, CancellationToken cancellationToken) =>
        await dbContext.VisaDocumentProcesses
            .FirstOrDefaultAsync(x => x.Id == processId && x.TenantId == tenantContext.TenantId, cancellationToken);

    public async Task<IReadOnlyList<VisaDocumentProcess>> GetActiveAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.VisaDocumentProcesses
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(VisaDocumentProcess process, CancellationToken cancellationToken) =>
        await dbContext.VisaDocumentProcesses.AddAsync(process, cancellationToken);

    public async Task SaveAsync(CancellationToken cancellationToken) =>
        await dbContext.SaveChangesAsync(cancellationToken);
}

using Microsoft.EntityFrameworkCore;
using Sense.Modules.Documents.Application.VisaDocuments;

namespace Sense.Modules.Documents.Infrastructure.Persistence.Visa;

/// <summary>
/// EF Core implementation of <see cref="IVisaSettingsProvider"/>: reads the tenant's single <c>VisaSettings</c> row
/// directly (no seeding here - unlike <see cref="VisaSettingsRepository"/>'s command-side use with
/// VisaSettingsProvisioning.EnsureAsync, a missing row on this read-only path just means "nothing configured yet",
/// which <see cref="Application.VisaDocuments.SubmitVisaDocumentRequestHandler"/> already treats as a valid,
/// user-facing "approver not configured" failure).
/// </summary>
public sealed class VisaSettingsProvider(DocumentsDbContext context) : IVisaSettingsProvider
{
    public async Task<Guid?> GetApproverEmployeeIdAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var settings = await FindAsync(tenantId, cancellationToken);
        return settings?.ApproverEmployeeId;
    }

    public async Task<IReadOnlyList<Guid>> GetTemplateDocumentIdsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var settings = await FindAsync(tenantId, cancellationToken);
        return settings?.TemplateDocumentIds ?? [];
    }

    // Explicit TenantId filter (redundant with the DbContext's own global filter for the ambient tenant, but
    // matches VisaDocumentRequestRepository.GetByEmployeeIdAsync's convention of trusting the caller-supplied
    // tenantId, not just ambient context).
    private Task<Domain.VisaDocuments.VisaSettings?> FindAsync(Guid tenantId, CancellationToken cancellationToken) =>
        context.VisaSettings.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);
}

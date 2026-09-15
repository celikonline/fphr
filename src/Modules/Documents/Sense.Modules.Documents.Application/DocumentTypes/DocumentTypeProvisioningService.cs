using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.DocumentTypes;

namespace Sense.Modules.Documents.Application.DocumentTypes;

/// <summary>
/// Seeds the tenant's document type catalog from <see cref="DocumentTypeSeed"/> the first time it's needed - there is
/// no per-module tenant-provisioning hook today, so this runs lazily from the first read/write that needs the
/// catalog (mirrors Leave's LeaveBalanceService.EnsureDefaultRuleSetAsync). Does not save; the caller (a query
/// handler or an integration event handler, neither of which runs inside the command pipeline's UnitOfWorkBehaviour)
/// must call its own IDocumentsUnitOfWork.SaveChangesAsync afterwards.
/// </summary>
public sealed class DocumentTypeProvisioningService(IDocumentTypeDefinitionRepository types)
{
    public async Task EnsureSeededAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (await types.AnyForTenantAsync(cancellationToken))
        {
            return;
        }

        foreach (var row in DocumentTypeSeed.SystemDefinitions())
        {
            types.Add(DocumentTypeDefinition.Create(
                tenantId, row.Code, row.Name, row.Stage, row.IsRequired, row.Condition, row.RenewalPeriodDays,
                row.Quantity, row.IsSensitive, row.DefaultRetentionDays, isSystem: true, row.SortOrder).Value);
        }
    }
}

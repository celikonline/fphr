using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.DocumentTypes;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Documents.Application.DocumentTypes;

/// <summary>Lists the tenant's document type catalog; seeds it on first read (<see cref="DocumentTypeProvisioningService"/>).</summary>
[RequiresPermission("documents.document_types.view")]
public sealed record ListDocumentTypesQuery(DocumentTypeStage? Stage, bool? IsActive) : IQuery<IReadOnlyList<DocumentTypeDto>>;

public sealed class ListDocumentTypesHandler(
    ITenantContext tenant,
    IDocumentTypeDefinitionRepository types,
    DocumentTypeProvisioningService provisioning,
    IDocumentsUnitOfWork unitOfWork) : IQueryHandler<ListDocumentTypesQuery, IReadOnlyList<DocumentTypeDto>>
{
    public async Task<Result<IReadOnlyList<DocumentTypeDto>>> Handle(ListDocumentTypesQuery query, CancellationToken cancellationToken)
    {
        if (!await types.AnyForTenantAsync(cancellationToken))
        {
            await provisioning.EnsureSeededAsync(tenant.TenantId, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var list = await types.ListAsync(query.Stage, query.IsActive, cancellationToken);
        return Result.Success<IReadOnlyList<DocumentTypeDto>>(list.Select(DocumentTypeDto.From).ToList());
    }
}

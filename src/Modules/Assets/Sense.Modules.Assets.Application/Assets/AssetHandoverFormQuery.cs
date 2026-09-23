using Sense.Modules.Assets.Domain;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Assets.Application.Assets;

/// <summary>Zimmet tutanağı PDF for one asset (11.3.08 md.10) - HR downloads, gets it signed, uploads it to the employee's Documents card by hand.</summary>
[RequiresPermission("assets.assets.view")]
public sealed record GetAssetHandoverFormQuery(Guid AssetId) : IQuery<ExportFileResult>;

public sealed class GetAssetHandoverFormHandler(ITenantContext tenant, IAssetHandoverFormProvider forms)
    : IQueryHandler<GetAssetHandoverFormQuery, ExportFileResult>
{
    public async Task<Result<ExportFileResult>> Handle(GetAssetHandoverFormQuery query, CancellationToken cancellationToken)
    {
        var file = await forms.RenderPdfAsync(tenant.TenantId, query.AssetId, cancellationToken);
        return file is null ? Error.NotFound(AssetsDomainErrors.NotFound) : file;
    }
}

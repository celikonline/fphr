using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Persistence;

namespace Sense.Modules.Assets.Application;

/// <summary>Assets module's UnitOfWork surface for the command pipeline; implemented by AssetsDbContext.</summary>
public interface IAssetsUnitOfWork : IUnitOfWork;

/// <summary>
/// Zimmet tutanağı (equipment handover form) PDF for a single asset - 11.3.08 md.10's "açık zimmet için tutanak
/// belgesi" printable form. Implemented in Sense.Modules.Assets.Infrastructure (ISimplePdfWriter, same pattern as
/// Employee's IWorkCertificateProvider). Null when the asset does not exist for this tenant. Producing this form is
/// a manual HR action for now (download, get it signed, upload to the employee's Documents card by hand) - it does
/// not yet auto-satisfy the "AssetHandoverForm" conditional document requirement (that needs Documents' own upload
/// pipeline wired to it, a separate, larger piece of work - see 11.3.08-zimmet-ekran-paritesi.md backlog).
/// </summary>
public interface IAssetHandoverFormProvider
{
    Task<ExportFileResult?> RenderPdfAsync(Guid tenantId, Guid assetId, CancellationToken cancellationToken = default);
}

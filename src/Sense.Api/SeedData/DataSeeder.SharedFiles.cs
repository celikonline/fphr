using Sense.Modules.Documents.Domain.SharedFiles;
using Sense.Modules.Identity.Domain.Roles;

namespace Sense.Api.SeedData;

/// <summary>senseik-028: demo rows for the shared company files area, metadata only - no real bytes are uploaded to
/// MinIO, matching <c>SeedSampleDocumentsAsync</c>'s approach for the employee-scoped Documents table (the seeder
/// marks each row Active with a synthetic ETag so it renders in the list without needing storage to hold anything).</summary>
public partial class DataSeeder
{
    private async Task SeedSharedCompanyFilesAsync(Guid tenantId, Guid uploadedBy, CancellationToken cancellationToken)
    {
        if (uploadedBy == Guid.Empty)
        {
            logger.LogWarning("No admin user available for shared company file seeding");
            return;
        }

        var everyoneFiles = new[]
        {
            ("Personel Yönetmeliği.pdf", "shared-files/personel-yonetmeligi.pdf", "application/pdf", 1024 * 512),
            ("Şirket Tanıtım Sunumu.pptx", "shared-files/sirket-tanitim.pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation", 1024 * 1024 * 2),
            ("İş Sağlığı ve Güvenliği Kılavuzu.pdf", "shared-files/isg-kilavuzu.pdf", "application/pdf", 1024 * 768),
        };

        foreach (var (fileName, fileKey, contentType, size) in everyoneFiles)
        {
            AddSeededSharedFile(tenantId, fileName, fileKey, contentType, size, uploadedBy, SharedFileVisibility.Everyone, []);
        }

        // Role-restricted example (senseik-028 acceptance: "sadece HR/Admin yükleyebilir gibi" also applies to who can *see* a given file).
        AddSeededSharedFile(
            tenantId,
            "Bordro Hesaplama Şablonu.xlsx",
            "shared-files/bordro-sablonu.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            1024 * 256,
            uploadedBy,
            SharedFileVisibility.RestrictedToRoles,
            [SystemRoleCodes.HR, SystemRoleCodes.PayrollSpecialist]);

        await documentsCtx.SaveChangesAsync(cancellationToken);
    }

    private void AddSeededSharedFile(
        Guid tenantId,
        string fileName,
        string fileKey,
        string contentType,
        long size,
        Guid uploadedBy,
        SharedFileVisibility visibility,
        IReadOnlyList<string> allowedRoles)
    {
        var result = SharedCompanyFile.Create(tenantId, fileName, fileKey, contentType, size, uploadedBy, visibility, allowedRoles);
        if (result.IsFailure)
        {
            logger.LogWarning("Skipped seeding shared company file {FileName}: {Error}", fileName, result.Error.Code);
            return;
        }

        var file = result.Value;
        file.ConfirmUpload($"etag_{Guid.NewGuid():N}", size);
        documentsCtx.SharedCompanyFiles.Add(file);
    }
}

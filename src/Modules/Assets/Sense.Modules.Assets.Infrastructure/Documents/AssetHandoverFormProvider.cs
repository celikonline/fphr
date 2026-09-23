using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Sense.Modules.Assets.Application;
using Sense.Modules.Assets.Infrastructure.Persistence;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Export;

namespace Sense.Modules.Assets.Infrastructure.Documents;

/// <summary>
/// <see cref="IAssetHandoverFormProvider"/>: zimmet tutanağı - equipment identity (category, serial, issue/return
/// date), employee identity, and signature boxes. Same ISimplePdfWriter pattern as Employee's
/// WorkCertificateProvider (çalışma belgesi). Company/issuer is intentionally omitted for this first pass - adding
/// it means a new Organization.Contracts reference for one header line, deferred until the form is wired into an
/// automated flow (see IAssetHandoverFormProvider's remarks).
/// </summary>
public sealed class AssetHandoverFormProvider(
    AssetsDbContext context, IEmployeeContactLookup contacts, ISimplePdfWriter pdfWriter, TimeProvider clock) : IAssetHandoverFormProvider
{
    private const string DateFormat = "dd.MM.yyyy";
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public async Task<ExportFileResult?> RenderPdfAsync(Guid tenantId, Guid assetId, CancellationToken cancellationToken = default)
    {
        var asset = await context.Assets.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.Id == assetId)
            .FirstOrDefaultAsync(cancellationToken);
        if (asset is null || asset.AssignedEmployeeId is not { } employeeId)
        {
            return null;
        }

        var category = await context.AssetCategories.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Id == asset.CategoryId)
            .FirstOrDefaultAsync(cancellationToken);

        var contact = (await contacts.GetContactsAsync(tenantId, [employeeId], cancellationToken)).FirstOrDefault();
        var fullName = contact is null ? "-" : $"{contact.FirstName} {contact.LastName}".Trim();

        var rows = new List<KeyValuePair<string, string>>
        {
            new("Çalışan", fullName),
            new("Kategori", category?.Name ?? "-"),
            new("Zimmet", asset.Name),
            new("Seri Numarası", asset.SerialNumber ?? "-"),
            new("Veriliş Tarihi", Format(asset.IssuedAt)),
        };
        if (asset.ReturnedAt is { } returnedAt)
        {
            rows.Add(new KeyValuePair<string, string>("İade Tarihi", Format(returnedAt)));
        }

        if (!string.IsNullOrWhiteSpace(asset.Description))
        {
            rows.Add(new KeyValuePair<string, string>("Açıklama", asset.Description));
        }

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var document = new SimplePdfDocument(
            "ZİMMET TUTANAĞI",
            Issuer: null,
            [
                new SimplePdfKeyValues(null, rows),
                new SimplePdfParagraph(
                    $"Yukarıda bilgileri yazılı ekipman, {fullName} tarafından teslim alınmış olup, iş bu tutanağın imzalanmasıyla " +
                    "kullanımına ilişkin sorumluluk kendisine geçmiştir. Ekipman, iş ilişkisinin sona ermesi veya işveren talebi " +
                    "hâlinde eksiksiz ve çalışır durumda iade edilecektir."),
                new SimplePdfParagraph($"Düzenleme tarihi: {today.ToString(DateFormat, Turkish)}"),
                new SimplePdfSignatures(["Çalışan\nİmza", "İşveren / Yetkili\nKaşe - İmza"]),
            ],
            Footer: "SenseİK tarafından oluşturulmuştur.");

        var fileName = $"zimmet-tutanagi_{Slug(fullName)}.pdf";
        return new ExportFileResult(fileName, "application/pdf", pdfWriter.Write(document));
    }

    private static string Format(DateOnly? date) => date?.ToString(DateFormat, Turkish) ?? "-";

    private static string Slug(string value)
    {
        var ascii = value.ToLower(Turkish)
            .Replace('ç', 'c').Replace('ğ', 'g').Replace('ı', 'i').Replace('ö', 'o').Replace('ş', 's').Replace('ü', 'u');
        var chars = ascii.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray();
        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
}

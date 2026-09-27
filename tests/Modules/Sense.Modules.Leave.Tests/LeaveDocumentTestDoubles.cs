using NSubstitute;
using Sense.Modules.Documents.Contracts;

namespace Sense.Modules.Leave.Tests;

/// <summary>Denetim A5-belge: IDocumentAttachmentLookup sahteleri (LeaveRequestRules'un yeni bağı).</summary>
internal static class DocumentLookupDoubles
{
    /// <summary>
    /// İzin veren sahte: istenen her kimlik Active belgeymiş gibi döner. Belge kuralı test dışı kalan
    /// fixture'lar etkilenmesin diye (sick gibi RequiresDocument türleri de sorunsuz gönderilir). Sahiplik alanları
    /// boş (<c>Guid.Empty</c>/null) kalır - bu sahteyi kullanan testler hiç belge eklemez (bkz. çağıran dosyalar).
    /// </summary>
    public static IDocumentAttachmentLookup Permissive()
    {
        var documents = Substitute.For<IDocumentAttachmentLookup>();
        documents.GetActiveAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<IReadOnlyCollection<Guid>>(1)
                .ToDictionary(id => id, id => new AttachedDocumentInfo(id, "test-belge.pdf", "application/pdf", 1024, Guid.Empty, null)));
        return documents;
    }

    /// <summary>Katı sahte: yalnız verilen kimlikler Active döner (eksik/silinmiş belge senaryoları için), sahibi <paramref name="uploadedBy"/>.</summary>
    public static IDocumentAttachmentLookup Only(Guid uploadedBy, params Guid[] activeIds)
    {
        var set = activeIds.ToHashSet();
        var documents = Substitute.For<IDocumentAttachmentLookup>();
        documents.GetActiveAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<IReadOnlyCollection<Guid>>(1)
                .Where(set.Contains)
                .ToDictionary(id => id, id => new AttachedDocumentInfo(id, "test-belge.pdf", "application/pdf", 1024, uploadedBy, null)));
        return documents;
    }
}

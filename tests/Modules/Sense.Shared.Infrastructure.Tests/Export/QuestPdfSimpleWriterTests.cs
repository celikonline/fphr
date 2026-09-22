using System.Text;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Infrastructure.Export;
using Xunit;

namespace Sense.Shared.Infrastructure.Tests.Export;

public sealed class QuestPdfSimpleWriterTests
{
    [Fact]
    public void Write_ProducesAPdf_WithEveryBlockKind_AndTurkishText()
    {
        var document = new SimplePdfDocument(
            "ÇALIŞMA BELGESİ",
            "Demo İnsan Kaynakları A.Ş.",
            [
                new SimplePdfKeyValues(null, [new("Adı Soyadı", "Şükrü Öztürk"), new("İşe başlama tarihi", "01.02.2020")]),
                new SimplePdfTable("Kesintiler", ["Kalem", "Tutar (TL)"], [["SGK İşçi Payı", "1.234,56"], ["Damga Vergisi"]], [1]),
                new SimplePdfParagraph("NET ÖDENEN: 25.000,00 TL", Bold: true),
                new SimplePdfSignatures(["İşveren\nKaşe - İmza", "Çalışan"]),
            ],
            "SenseİK");

        var bytes = new QuestPdfSimpleWriter().Write(document);

        Assert.True(bytes.Length > 1000);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
    }
}

namespace Sense.Shared.Contracts.Export;

/// <summary>Bir dışa aktarım hücresinin biçimlendirmesi (tarih/sayı formatı için); değerin CLR tipi zaten
/// <see cref="object"/> olarak taşınır, bu yalnızca hücre biçimini belirler.</summary>
public enum ExportColumnType
{
    Text,
    Number,
    Date,
    DateTime,
    Boolean,
}

/// <summary>Dışa aktarılacak tek bir sütun: başlık (zaten yerelleştirilmiş, çağıran sorumlu) + biçim.</summary>
public sealed record ExportColumn(string Header, ExportColumnType Type = ExportColumnType.Text);

/// <summary>Dışa aktarılacak tek bir sayfa/tablo: sütun tanımları ve sütunlarla birebir sırayla hücre değerleri.
/// Hücre değeri tipi <see cref="ExportColumn.Type"/> ile uyumlu olmalı (örn. Date için <see cref="DateOnly"/>/<see cref="DateTime"/>);
/// null hücre boş bırakılır.</summary>
public sealed record ExportTable(string SheetName, IReadOnlyList<ExportColumn> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows);

/// <summary>Üretilen dışa aktarım dosyası: indirme için dosya adı, MIME tipi ve ham içerik.</summary>
public sealed record ExportFileResult(string FileName, string ContentType, byte[] Content);

/// <summary>
/// Modüller arası ortak "tablo -> Excel" dışa aktarma portu (Faz 1 altyapı sözleşmesi, TB-1). Bir modülün rapor
/// *verisini* üretmesi (örn. Reporting modülünün <c>IReportProvider</c>'ı) ile o veriyi bir dosya *biçimine*
/// çevirmek ayrı sorumluluklardır; tipik akış veri üretimi -&gt; <see cref="ExportTable"/> -&gt; bu port'tur.
/// Implementasyon <c>Sense.Shared.Infrastructure</c>'dadır (ClosedXML); modüller yalnızca bu Contracts projesine
/// referans verir (Modules_TalkOnlyThroughContracts).
/// </summary>
public interface IReportExporter
{
    /// <summary>Tek sayfalık bir tabloyu xlsx byte[]'a çevirir; dosya adı sayfa adından türetilir.</summary>
    ExportFileResult ExportToExcel(ExportTable table, CancellationToken cancellationToken = default);

    /// <summary>Birden fazla sayfayı tek bir çalışma kitabında xlsx byte[]'a çevirir.</summary>
    ExportFileResult ExportToExcel(string fileName, IReadOnlyList<ExportTable> sheets, CancellationToken cancellationToken = default);
}

/// <summary>
/// Şablon anahtarı + model verisinden belge (pdf) üreten port (Faz 1 altyapı sözleşmesi, TB-1). Bu turda yalnızca
/// sözleşme tanımlanır; ClosedXML/QuestPDF tabanlı implementasyon ayrı bir turda eklenecektir (bkz. rapor).
/// </summary>
public interface IDocumentRenderer
{
    /// <summary>Kayıtlı bir şablonu (ör. "documents.missingDocumentsNotice") verilen model ile PDF'e render eder.</summary>
    Task<ExportFileResult> RenderPdfAsync(string templateKey, IReadOnlyDictionary<string, object?> model, CancellationToken cancellationToken = default);
}

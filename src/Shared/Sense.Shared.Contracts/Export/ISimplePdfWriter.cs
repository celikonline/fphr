namespace Sense.Shared.Contracts.Export;

/// <summary>One block of a <see cref="SimplePdfDocument"/>, rendered top to bottom.</summary>
public abstract record SimplePdfBlock;

/// <summary>A paragraph of body text; <paramref name="Bold"/> for emphasis lines (e.g. "NET ÖDENEN").</summary>
public sealed record SimplePdfParagraph(string Text, bool Bold = false) : SimplePdfBlock;

/// <summary>A labelled list of "label: value" rows under an optional heading (e.g. employee identity data).</summary>
public sealed record SimplePdfKeyValues(string? Heading, IReadOnlyList<KeyValuePair<string, string>> Rows) : SimplePdfBlock;

/// <summary>
/// A table under an optional heading. <paramref name="RightAlignedColumns"/> are zero-based column indexes rendered
/// right-aligned (amounts); rows must have as many cells as <paramref name="Columns"/>.
/// </summary>
public sealed record SimplePdfTable(
    string? Heading,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    IReadOnlyList<int>? RightAlignedColumns = null) : SimplePdfBlock;

/// <summary>Signature boxes side by side at the end of a document (e.g. "İşveren / Kaşe - İmza").</summary>
public sealed record SimplePdfPageBreak : SimplePdfBlock;

public sealed record SimplePdfSignatures(IReadOnlyList<string> Labels) : SimplePdfBlock;

/// <summary>A full-width shaded band that opens a section of a multi-section document (e.g. employee card PDF).</summary>
public sealed record SimplePdfSectionHeading(string Text) : SimplePdfBlock;

/// <summary>
/// A plain A4 business document: header (issuer + title), blocks, and a footer line. Texts are already localized by
/// the caller; the writer only lays them out.
/// </summary>
public sealed record SimplePdfDocument(
    string Title,
    string? Issuer,
    IReadOnlyList<SimplePdfBlock> Blocks,
    string? Footer = null,
    bool Landscape = false);

/// <summary>
/// Structured document → PDF bytes (QuestPDF implementation in Sense.Shared.Infrastructure). Used for documents a module
/// hands out or attaches to e-mails: ücret pusulası (Payroll), çalışma belgesi (Employee). Stateless and thread-safe.
/// </summary>
public interface ISimplePdfWriter
{
    byte[] Write(SimplePdfDocument document);
}

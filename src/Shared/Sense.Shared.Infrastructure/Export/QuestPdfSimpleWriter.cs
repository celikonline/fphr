using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Sense.Shared.Contracts.Export;

namespace Sense.Shared.Infrastructure.Export;

/// <summary><see cref="ISimplePdfWriter"/> on QuestPDF (Community licence): A4 portrait, bundled Lato font (covers Turkish letters).</summary>
public sealed class QuestPdfSimpleWriter : ISimplePdfWriter
{
    private const float PageMargin = 36;
    private const float BaseFontSize = 10;
    private const float TitleFontSize = 16;
    private const float HeadingFontSize = 11;
    private const float SectionFontSize = 12;
    private const float BlockSpacing = 14;
    private const float SignatureBoxHeight = 64;
    private const string MutedColor = "#555555";
    private const string BorderColor = "#BBBBBB";
    private const string HeaderFill = "#F0F2F5";

    static QuestPdfSimpleWriter()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Write(SimplePdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(PageMargin);
            page.DefaultTextStyle(x => x.FontSize(BaseFontSize));

            page.Header().Column(header =>
            {
                if (!string.IsNullOrWhiteSpace(document.Issuer))
                {
                    header.Item().Text(document.Issuer).FontColor(MutedColor);
                }

                header.Item().PaddingTop(4).Text(document.Title).FontSize(TitleFontSize).Bold();
                header.Item().PaddingTop(6).LineHorizontal(1).LineColor(BorderColor);
            });

            page.Content().PaddingTop(BlockSpacing).Column(column =>
            {
                column.Spacing(BlockSpacing);
                foreach (var block in document.Blocks)
                {
                    column.Item().Element(item => Render(item, block));
                }
            });

            page.Footer().Row(row =>
            {
                row.RelativeItem().Text(document.Footer ?? string.Empty).FontSize(BaseFontSize - 2).FontColor(MutedColor);
                row.ConstantItem(60).AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(x => x.FontSize(BaseFontSize - 2).FontColor(MutedColor));
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
            });
        })).GeneratePdf();
    }

    private static void Render(IContainer container, SimplePdfBlock block)
    {
        switch (block)
        {
            case SimplePdfParagraph paragraph:
                var span = container.Text(paragraph.Text);
                if (paragraph.Bold)
                {
                    span.Bold();
                }

                break;
            case SimplePdfKeyValues keyValues:
                RenderKeyValues(container, keyValues);
                break;
            case SimplePdfTable table:
                RenderTable(container, table);
                break;
            case SimplePdfSignatures signatures:
                RenderSignatures(container, signatures);
                break;
            case SimplePdfSectionHeading section:
                container.PaddingTop(4).Background(HeaderFill).PaddingVertical(5).PaddingHorizontal(6)
                    .Text(section.Text).FontSize(SectionFontSize).Bold();
                break;
            default:
                throw new NotSupportedException($"PDF block '{block.GetType().Name}' is not supported.");
        }
    }

    private static void RenderKeyValues(IContainer container, SimplePdfKeyValues block) =>
        container.Column(column =>
        {
            Heading(column, block.Heading);
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(2);
                    c.RelativeColumn(3);
                });
                foreach (var (label, value) in block.Rows)
                {
                    table.Cell().PaddingVertical(2).Text(label).FontColor(MutedColor);
                    table.Cell().PaddingVertical(2).Text(value);
                }
            });
        });

    private static void RenderTable(IContainer container, SimplePdfTable block)
    {
        var rightAligned = block.RightAlignedColumns ?? [];
        container.Column(column =>
        {
            Heading(column, block.Heading);
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    for (var i = 0; i < block.Columns.Count; i++)
                    {
                        c.RelativeColumn(i == 0 ? 3 : 2);
                    }
                });
                table.Header(header =>
                {
                    for (var i = 0; i < block.Columns.Count; i++)
                    {
                        var cell = header.Cell().Background(HeaderFill).Padding(4);
                        (rightAligned.Contains(i) ? cell.AlignRight() : cell).Text(block.Columns[i]).Bold();
                    }
                });
                foreach (var row in block.Rows)
                {
                    for (var i = 0; i < block.Columns.Count; i++)
                    {
                        var cell = table.Cell().BorderBottom(0.5f).BorderColor(BorderColor).Padding(4);
                        (rightAligned.Contains(i) ? cell.AlignRight() : cell).Text(i < row.Count ? row[i] : string.Empty);
                    }
                }
            });
        });
    }

    private static void RenderSignatures(IContainer container, SimplePdfSignatures block) =>
        container.PaddingTop(BlockSpacing).Row(row =>
        {
            row.Spacing(BlockSpacing);
            foreach (var label in block.Labels)
            {
                row.RelativeItem().Border(0.5f).BorderColor(BorderColor).Height(SignatureBoxHeight).Padding(6)
                    .Text(label).FontColor(MutedColor);
            }
        });

    private static void Heading(ColumnDescriptor column, string? heading)
    {
        if (!string.IsNullOrWhiteSpace(heading))
        {
            column.Item().PaddingBottom(4).Text(heading).FontSize(HeadingFontSize).Bold();
        }
    }
}

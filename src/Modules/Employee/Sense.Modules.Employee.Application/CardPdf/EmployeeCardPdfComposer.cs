using System.Globalization;
using Sense.Modules.Employee.Contracts.CardPdf;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Employee.Application.CardPdf;

/// <summary>
/// Builds the employee card PDF from every registered <see cref="IEmployeeCardPdfSection"/>: validates the request,
/// skips providers of disabled modules, drops sections the caller may not see, and renders the rest in card order.
/// </summary>
public sealed class EmployeeCardPdfComposer(
    IEnumerable<IEmployeeCardPdfSection> sections,
    IModuleAccessService modules,
    ISimplePdfWriter writer,
    TimeProvider clock)
{
    private const string EmptySectionText = "Bu bölümde kayıt yok.";
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    public async Task<Result<ExportFileResult>> ComposeAsync(
        EmployeeCardPdfRequest request,
        string employeeName,
        IReadOnlyCollection<string> sectionKeys,
        CancellationToken cancellationToken)
    {
        if (Validate(request, sectionKeys) is { } error)
        {
            return error;
        }

        // Spec: the card PDF is always Turkish, regardless of the caller's Accept-Language/UI culture (section
        // providers and Table/EnumText localize with the ambient culture).
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = Turkish;
        CultureInfo.CurrentUICulture = Turkish;
        try
        {
            var blocks = new List<SimplePdfBlock>
            {
                EmployeeCardPdfFormat.KeyValues(
                    null,
                    ("Çalışan", employeeName),
                    ("Dönem", EmployeeCardPdfFormat.PeriodText(request.From, request.To))),
            };
            var anySection = false;
            foreach (var key in EmployeeCardPdfSectionKeys.All.Where(sectionKeys.Contains))
            {
                var sectionBlocks = await BuildSectionAsync(key, request, cancellationToken);
                if (sectionBlocks is null)
                {
                    continue;
                }

                anySection = true;
                blocks.Add(new SimplePdfSectionHeading(EmployeeCardPdfSectionKeys.Title(key)));
                blocks.AddRange(sectionBlocks.Count == 0 ? [new SimplePdfParagraph(EmptySectionText)] : sectionBlocks);
            }

            if (!anySection)
            {
                return Error.Forbidden(EmployeeErrors.CardPdfNoSections);
            }

            var now = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Istanbul);
            var document = new SimplePdfDocument(
                "Çalışan Kartı",
                employeeName,
                blocks,
                $"Oluşturma: {now.ToString("dd.MM.yyyy HH:mm", Turkish)}");
            var fileName = $"calisan-karti-{Slug(employeeName)}-{now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.pdf";
            return new ExportFileResult(fileName, "application/pdf", writer.Write(document));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    private static Error? Validate(EmployeeCardPdfRequest request, IReadOnlyCollection<string> sectionKeys)
    {
        if (sectionKeys.Count == 0)
        {
            return Error.Validation(EmployeeErrors.CardPdfSectionsRequired);
        }

        var unknown = sectionKeys.FirstOrDefault(key => !EmployeeCardPdfSectionKeys.All.Contains(key));
        if (unknown is not null)
        {
            return Error.Validation(EmployeeErrors.CardPdfUnknownSection, ("section", unknown));
        }

        return request.From is { } from && request.To is { } to && from > to
            ? Error.Validation(EmployeeErrors.CardPdfPeriodInvalid)
            : null;
    }

    private async Task<IReadOnlyList<SimplePdfBlock>?> BuildSectionAsync(
        string key, EmployeeCardPdfRequest request, CancellationToken cancellationToken)
    {
        List<SimplePdfBlock>? result = null;
        foreach (var provider in sections.Where(s => s.Key == key).OrderBy(s => s.Order))
        {
            if (provider.ModuleKey is { } module && !await modules.IsEnabledAsync(module, cancellationToken))
            {
                continue;
            }

            var built = await provider.BuildAsync(request, cancellationToken);
            if (built.Blocks is { } provided)
            {
                (result ??= []).AddRange(provided);
            }
        }

        return result;
    }

    private static string Slug(string value)
    {
        var ascii = value.ToLower(Turkish)
            .Replace('ç', 'c').Replace('ğ', 'g').Replace('ı', 'i').Replace('ö', 'o').Replace('ş', 's').Replace('ü', 'u');
        var chars = ascii.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray();
        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
}

using Microsoft.Extensions.Localization;
using Sense.Modules.Documents.Application.DocumentTypes;
using Sense.Modules.Documents.Domain.DocumentTypes;
using Sense.Modules.Employee.Contracts.CardPdf;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.Export;
using static Sense.Modules.Employee.Contracts.CardPdf.EmployeeCardPdfFormat;

namespace Sense.Modules.Documents.Infrastructure.CardPdf;

/// <summary>Card PDF "Evraklar": requirement list with status (state data, not period-filtered; no file contents).</summary>
public sealed class DocumentsCardPdfSection(IDispatcher dispatcher, IStringLocalizer<SharedResource> localizer) : IEmployeeCardPdfSection
{
    public string Key => EmployeeCardPdfSectionKeys.Documents;

    public int Order => 0;

    public string? ModuleKey => null;

    public async Task<EmployeeCardPdfSectionResult> BuildAsync(EmployeeCardPdfRequest request, CancellationToken cancellationToken)
    {
        var result = request.IsOwnCard
            ? await dispatcher.Query(new GetMyDocumentRequirementsQuery(), cancellationToken)
            : await dispatcher.Query(new GetEmployeeDocumentRequirementsQuery(request.EmployeeId), cancellationToken);
        return EmployeeCardPdfSectionResult.Combine(EmployeeCardPdfSectionResult.Part(result, rows => Table(
            "Evrak gereksinimleri",
            ["Evrak", "Aşama", "Durum", "Son tarih", "Geçerlilik sonu", "Muafiyet nedeni"],
            rows.OrderBy(r => r.Stage).ThenBy(r => r.DocumentTypeName).Select(r => (IReadOnlyList<string>)
            [
                r.DocumentTypeName, localizer.EnumText<DocumentTypeStage>(r.Stage) ?? Empty, localizer.EnumText<DocumentRequirementStatus>(r.Status) ?? Empty,
                Date(r.DueDate), Date(r.ExpiryDate), Text(r.WaivedReason),
            ]))));
    }
}

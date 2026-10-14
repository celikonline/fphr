using Sense.Modules.Employee.Application.Notes;
using Sense.Modules.Employee.Contracts.CardPdf;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Messaging;
using static Sense.Modules.Employee.Contracts.CardPdf.EmployeeCardPdfFormat;

namespace Sense.Modules.Employee.Infrastructure.CardPdf;

/// <summary>Prints the employee's HR notes using the same employee-scope permission as the card tab.</summary>
public sealed class EmployeeNotesCardPdfSection(IDispatcher dispatcher) : IEmployeeCardPdfSection
{
    public string Key => EmployeeCardPdfSectionKeys.Notes;

    public int Order => 0;

    public string? ModuleKey => null;

    public async Task<EmployeeCardPdfSectionResult> BuildAsync(EmployeeCardPdfRequest request, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Query(new ListEmployeeNotesQuery(request.EmployeeId, null, Page: 1, PageSize: 100), cancellationToken);
        return new EmployeeCardPdfSectionResult(EmployeeCardPdfSectionResult.Part(result, page => Table(
            "Çalışan notları",
            ["Başlık", "Önem", "Tarih", "Yazan", "Not"],
            page.Items
                .Where(note => InPeriod(DateOnly.FromDateTime(note.CreatedAt), null, request.From, request.To))
                .OrderByDescending(note => note.CreatedAt)
                .Select(note => (IReadOnlyList<string>)[
                    Text(note.Title),
                    note.Importance switch
                    {
                        EmployeeNoteImportance.Low => "Düşük",
                        EmployeeNoteImportance.Normal => "Normal",
                        EmployeeNoteImportance.High => "Yüksek",
                        _ => Text(note.Importance.ToString()),
                    },
                    Timestamp(note.CreatedAt),
                    Text(note.AuthorName),
                    Text(note.Content),
                ]))));
    }
}

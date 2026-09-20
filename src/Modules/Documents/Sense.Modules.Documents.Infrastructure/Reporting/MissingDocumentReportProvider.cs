using Microsoft.Extensions.Localization;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.DocumentTypes;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.Export;

namespace Sense.Modules.Documents.Infrastructure.Reporting;

/// <summary>
/// Reporting module provider (Faz 4 #70, Raporlar → İdari) over the same data as D2's missing-document report page
/// (GetMissingDocumentReportQuery/ListReportAsync) - every requirement across every status, not paged, as an Excel
/// workbook with localized headers (previously an English-only CSV). Rows whose document type no longer exists are skipped.
/// </summary>
public sealed class MissingDocumentReportProvider(
    IEmployeeDocumentRequirementRepository requirements,
    IDocumentTypeDefinitionRepository types,
    IEmployeeContactLookup employeeContacts,
    IReportExporter exporter,
    IStringLocalizer<SharedResource> localizer,
    TimeProvider clock) : IReportProvider
{
    public const string ReportKey = "documents.missingDocuments";

    public string Key => ReportKey;

    public string CategoryKey => ReportCategories.Administrative;

    public string? RequiredPermission => "documents.requirements.view";

    public async Task<ReportFileResult> GenerateAsync(Guid tenantId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var (items, _) = await requirements.ListReportAsync(status: null, skip: 0, take: int.MaxValue, employeeIds: null, cancellationToken);

        var typeMap = new Dictionary<Guid, DocumentTypeDefinition>();
        foreach (var typeId in items.Select(i => i.DocumentTypeId).Distinct())
        {
            var type = await types.GetByIdAsync(typeId, cancellationToken);
            if (type.IsSuccess)
            {
                typeMap[typeId] = type.Value;
            }
        }

        var employeeIds = items.Select(i => i.EmployeeId).Distinct().ToList();
        var contacts = (await employeeContacts.GetContactsAsync(tenantId, employeeIds, cancellationToken)).ToDictionary(c => c.EmployeeId);

        var columns = new List<ExportColumn>
        {
            new(localizer.Column("firstName")),
            new(localizer.Column("lastName")),
            new(localizer.Column("status")),
            new(localizer.Column("documentType")),
            new(localizer.Column("documentStage")),
            new(localizer.Column("requirementStatus")),
            new(localizer.Column("dueDate"), ExportColumnType.Date),
        };

        var rows = new List<IReadOnlyList<object?>>(items.Count);
        foreach (var item in items)
        {
            if (!typeMap.TryGetValue(item.DocumentTypeId, out var type))
            {
                continue;
            }

            var contact = contacts.GetValueOrDefault(item.EmployeeId);
            rows.Add(
            [
                contact?.FirstName,
                contact?.LastName,
                localizer.EnumText("EmployeeStatus", contact?.Status),
                type.Name,
                localizer.EnumText<DocumentTypeStage>(type.Stage),
                localizer.EnumText<DocumentRequirementStatus>(item.Status),
                item.DueDate,
            ]);
        }

        var sheet = localizer.Sheet("missingDocuments");
        var file = exporter.ExportToExcel($"{sheet} {clock.GetUtcNow():yyyy-MM-dd}.xlsx", [new ExportTable(sheet, columns, rows)], cancellationToken);
        return new ReportFileResult(file.FileName, file.ContentType, file.Content);
    }
}

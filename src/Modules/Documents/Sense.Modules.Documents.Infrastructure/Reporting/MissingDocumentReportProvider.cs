using System.Text;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.DocumentTypes;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Reporting.Contracts;

namespace Sense.Modules.Documents.Infrastructure.Reporting;

/// <summary>
/// Reporting module provider (Faz 4 #70) over the same data as D2's missing-document report page
/// (GetMissingDocumentReportQuery/ListReportAsync) - a full CSV export across every status, not paged.
/// </summary>
public sealed class MissingDocumentReportProvider(
    IEmployeeDocumentRequirementRepository requirements,
    IDocumentTypeDefinitionRepository types,
    IEmployeeContactLookup employeeContacts) : IReportProvider
{
    public string Key => "documents.missingDocuments";

    public string? RequiredPermission => "documents.requirements.view";

    public async Task<ReportFileResult> GenerateAsync(Guid tenantId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var (items, _) = await requirements.ListReportAsync(status: null, skip: 0, take: int.MaxValue, cancellationToken);

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
        var contacts = await employeeContacts.GetContactsAsync(tenantId, employeeIds, cancellationToken);
        var nameByEmployeeId = contacts.ToDictionary(c => c.EmployeeId, c => $"{c.FirstName} {c.LastName}".Trim());

        var csv = new StringBuilder("Employee,DocumentType,Stage,Status,DueDate\n");
        foreach (var item in items)
        {
            if (!typeMap.TryGetValue(item.DocumentTypeId, out var type))
            {
                continue;
            }

            var name = nameByEmployeeId.GetValueOrDefault(item.EmployeeId, string.Empty);
            csv.Append(CsvField(name)).Append(',')
               .Append(CsvField(type.Name)).Append(',')
               .Append(type.Stage).Append(',')
               .Append(item.Status).Append(',')
               .Append(item.DueDate?.ToString("yyyy-MM-dd") ?? string.Empty)
               .Append('\n');
        }

        var content = Encoding.UTF8.GetBytes(csv.ToString());
        return new ReportFileResult($"missing-documents-{DateTime.UtcNow:yyyyMMdd}.csv", "text/csv", content);
    }

    private static string CsvField(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}

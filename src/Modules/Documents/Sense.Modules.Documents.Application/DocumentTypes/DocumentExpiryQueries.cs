using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.Documents;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Documents.Application.DocumentTypes;

/// <summary>Paged report of documents that are expiring soon or already expired (<c>GET /api/v1/documents/expiring</c>,
/// S-6 - sözleşme Faz 4 FE ile bu turda paylaşıldı). Uses the same permission as the missing-document report - both
/// are HR/manager-facing document oversight views.</summary>
[RequiresPermission("documents.requirements.view")]
public sealed record GetExpiringDocumentsQuery(int WithinDays, DocumentExpiryState? State, PagedQuery Paging) : IQuery<PagedResult<ExpiringDocumentDto>>;

public sealed class GetExpiringDocumentsHandler(
    IEmployeeDocumentRequirementRepository requirements,
    IDocumentTypeDefinitionRepository types,
    IEmployeeContactLookup employeeContacts,
    ITenantContext tenant,
    TimeProvider clock) : IQueryHandler<GetExpiringDocumentsQuery, PagedResult<ExpiringDocumentDto>>
{
    public async Task<Result<PagedResult<ExpiringDocumentDto>>> Handle(GetExpiringDocumentsQuery query, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var withinDays = query.WithinDays > 0 ? query.WithinDays : DocumentsLimits.DefaultExpiryReportWindowDays;

        var (rows, total) = await requirements.ListExpiringAsync(
            today, withinDays, query.State, query.Paging.Skip, query.Paging.PageSize, cancellationToken);

        var typeMap = new Dictionary<Guid, string>();
        foreach (var typeId in rows.Select(r => r.DocumentTypeId).Distinct())
        {
            var type = await types.GetByIdAsync(typeId, cancellationToken);
            if (type.IsSuccess)
            {
                typeMap[typeId] = type.Value.Name;
            }
        }

        var employeeIds = rows.Select(r => r.EmployeeId).Distinct().ToList();
        var contacts = await employeeContacts.GetContactsAsync(tenant.TenantId, employeeIds, cancellationToken);
        var nameByEmployeeId = contacts.ToDictionary(c => c.EmployeeId, c => $"{c.FirstName} {c.LastName}".Trim());

        var dtos = rows
            .Where(r => typeMap.ContainsKey(r.DocumentTypeId))
            .Select(r => new ExpiringDocumentDto(
                r.DocumentId,
                r.EmployeeId,
                nameByEmployeeId.GetValueOrDefault(r.EmployeeId, string.Empty),
                r.DocumentTypeId,
                typeMap[r.DocumentTypeId],
                r.ExpiryDate,
                r.ExpiryDate.DayNumber - today.DayNumber))
            .ToList();

        return new PagedResult<ExpiringDocumentDto>(dtos, query.Paging.Page, query.Paging.PageSize, total);
    }
}

using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.DocumentTypes;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Documents.Application.DocumentTypes;

internal static class DocumentsPermissionKeys
{
    public const string RequirementsView = "documents.requirements.view";
}

internal static class EmployeeDocumentRequirementMapping
{
    /// <summary>Maps requirements to DTOs, resolving <c>ExpiryDate</c> for all linked documents in a single
    /// batched query (no N+1) - see S-6/faz-04 gereksinim DTO'sunda geçerlilik tarihi.</summary>
    public static async Task<IReadOnlyList<EmployeeDocumentRequirementDto>> ToDtosAsync(
        IReadOnlyList<EmployeeDocumentRequirement> requirements,
        IDocumentTypeDefinitionRepository types,
        IDocumentRepository documents,
        CancellationToken cancellationToken)
    {
        var documentIds = requirements
            .Where(r => r.DocumentId is not null)
            .Select(r => r.DocumentId!.Value)
            .Distinct()
            .ToList();
        var expiryDates = await documents.GetExpiryDatesByIdsAsync(documentIds, cancellationToken);

        var dtos = new List<EmployeeDocumentRequirementDto>(requirements.Count);
        foreach (var requirement in requirements)
        {
            var type = await types.GetByIdAsync(requirement.DocumentTypeId, cancellationToken);
            if (type.IsSuccess)
            {
                var expiryDate = requirement.DocumentId is { } documentId
                    ? expiryDates.GetValueOrDefault(documentId)
                    : null;
                dtos.Add(EmployeeDocumentRequirementDto.From(requirement, type.Value, expiryDate));
            }
        }

        return dtos;
    }
}

[RequiresPermission("documents.requirements.view")]
public sealed record GetEmployeeDocumentRequirementsQuery(Guid EmployeeId) : IQuery<IReadOnlyList<EmployeeDocumentRequirementDto>>;

public sealed class GetEmployeeDocumentRequirementsHandler(
    IEmployeeDocumentRequirementRepository requirements, IDocumentTypeDefinitionRepository types, IDocumentRepository documents,
    IDataScopeService dataScope)
    : IQueryHandler<GetEmployeeDocumentRequirementsQuery, IReadOnlyList<EmployeeDocumentRequirementDto>>
{
    public async Task<Result<IReadOnlyList<EmployeeDocumentRequirementDto>>> Handle(GetEmployeeDocumentRequirementsQuery query, CancellationToken cancellationToken)
    {
        if (!await dataScope.CanAccessEmployeeAsync(DocumentsPermissionKeys.RequirementsView, query.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(DocumentsErrors.EmployeeNotInScope);
        }

        var list = await requirements.ListByEmployeeAsync(query.EmployeeId, cancellationToken);
        return Result.Success(await EmployeeDocumentRequirementMapping.ToDtosAsync(list, types, documents, cancellationToken));
    }
}

/// <summary>Self-service: the caller's own document requirements.</summary>
[RequiresPermission("documents.my.requirements")]
public sealed record GetMyDocumentRequirementsQuery : IQuery<IReadOnlyList<EmployeeDocumentRequirementDto>>;

public sealed class GetMyDocumentRequirementsHandler(
    ICurrentUser currentUser, IEmployeeDocumentRequirementRepository requirements, IDocumentTypeDefinitionRepository types, IDocumentRepository documents)
    : IQueryHandler<GetMyDocumentRequirementsQuery, IReadOnlyList<EmployeeDocumentRequirementDto>>
{
    public async Task<Result<IReadOnlyList<EmployeeDocumentRequirementDto>>> Handle(GetMyDocumentRequirementsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(DocumentsErrors.NoEmployeeForUser);
        }

        var list = await requirements.ListByEmployeeAsync(employeeId, cancellationToken);
        return Result.Success(await EmployeeDocumentRequirementMapping.ToDtosAsync(list, types, documents, cancellationToken));
    }
}

/// <summary>Paged report of missing/uploaded/waived requirements across the tenant, with employee name and document type info.</summary>
[RequiresPermission("documents.requirements.view")]
public sealed record GetMissingDocumentReportQuery(DocumentRequirementStatus? Status, PagedQuery Paging) : IQuery<PagedResult<MissingDocumentReportItemDto>>;

public sealed class GetMissingDocumentReportHandler(
    IEmployeeDocumentRequirementRepository requirements,
    IDocumentTypeDefinitionRepository types,
    IEmployeeContactLookup employeeContacts,
    ITenantContext tenant,
    IDataScopeService dataScope) : IQueryHandler<GetMissingDocumentReportQuery, PagedResult<MissingDocumentReportItemDto>>
{
    public async Task<Result<PagedResult<MissingDocumentReportItemDto>>> Handle(GetMissingDocumentReportQuery query, CancellationToken cancellationToken)
    {
        // null = kapsam kısıtı yok (Company); aksi halde yalnız görünür çalışanların gereksinimleri raporlanır.
        var visible = await dataScope.GetVisibleEmployeeIdsAsync(DocumentsPermissionKeys.RequirementsView, cancellationToken);
        var (items, total) = await requirements.ListReportAsync(query.Status, query.Paging.Skip, query.Paging.PageSize, visible, cancellationToken);

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
        var contacts = await employeeContacts.GetContactsAsync(tenant.TenantId, employeeIds, cancellationToken);
        var nameByEmployeeId = contacts.ToDictionary(c => c.EmployeeId, c => $"{c.FirstName} {c.LastName}".Trim());

        var dtos = items
            .Where(i => typeMap.ContainsKey(i.DocumentTypeId))
            .Select(i =>
            {
                var type = typeMap[i.DocumentTypeId];
                return new MissingDocumentReportItemDto(
                    i.Id, i.EmployeeId, nameByEmployeeId.GetValueOrDefault(i.EmployeeId, string.Empty),
                    type.Code, type.Name, type.Stage, i.Status, i.DueDate);
            })
            .ToList();

        return new PagedResult<MissingDocumentReportItemDto>(dtos, query.Paging.Page, query.Paging.PageSize, total);
    }
}

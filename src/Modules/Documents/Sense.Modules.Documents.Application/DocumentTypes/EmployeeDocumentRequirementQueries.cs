using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.DocumentTypes;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Documents.Application.DocumentTypes;

internal static class EmployeeDocumentRequirementMapping
{
    public static async Task<IReadOnlyList<EmployeeDocumentRequirementDto>> ToDtosAsync(
        IReadOnlyList<EmployeeDocumentRequirement> requirements, IDocumentTypeDefinitionRepository types, CancellationToken cancellationToken)
    {
        var dtos = new List<EmployeeDocumentRequirementDto>(requirements.Count);
        foreach (var requirement in requirements)
        {
            var type = await types.GetByIdAsync(requirement.DocumentTypeId, cancellationToken);
            if (type.IsSuccess)
            {
                dtos.Add(EmployeeDocumentRequirementDto.From(requirement, type.Value));
            }
        }

        return dtos;
    }
}

[RequiresPermission("documents.requirements.view")]
public sealed record GetEmployeeDocumentRequirementsQuery(Guid EmployeeId) : IQuery<IReadOnlyList<EmployeeDocumentRequirementDto>>;

public sealed class GetEmployeeDocumentRequirementsHandler(IEmployeeDocumentRequirementRepository requirements, IDocumentTypeDefinitionRepository types)
    : IQueryHandler<GetEmployeeDocumentRequirementsQuery, IReadOnlyList<EmployeeDocumentRequirementDto>>
{
    public async Task<Result<IReadOnlyList<EmployeeDocumentRequirementDto>>> Handle(GetEmployeeDocumentRequirementsQuery query, CancellationToken cancellationToken)
    {
        var list = await requirements.ListByEmployeeAsync(query.EmployeeId, cancellationToken);
        return Result.Success(await EmployeeDocumentRequirementMapping.ToDtosAsync(list, types, cancellationToken));
    }
}

/// <summary>Self-service: the caller's own document requirements.</summary>
[RequiresPermission("documents.my.requirements")]
public sealed record GetMyDocumentRequirementsQuery : IQuery<IReadOnlyList<EmployeeDocumentRequirementDto>>;

public sealed class GetMyDocumentRequirementsHandler(
    ICurrentUser currentUser, IEmployeeDocumentRequirementRepository requirements, IDocumentTypeDefinitionRepository types)
    : IQueryHandler<GetMyDocumentRequirementsQuery, IReadOnlyList<EmployeeDocumentRequirementDto>>
{
    public async Task<Result<IReadOnlyList<EmployeeDocumentRequirementDto>>> Handle(GetMyDocumentRequirementsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(DocumentsErrors.NoEmployeeForUser);
        }

        var list = await requirements.ListByEmployeeAsync(employeeId, cancellationToken);
        return Result.Success(await EmployeeDocumentRequirementMapping.ToDtosAsync(list, types, cancellationToken));
    }
}

/// <summary>Paged report of missing/uploaded/waived requirements across the tenant, with employee name and document type info.</summary>
[RequiresPermission("documents.requirements.view")]
public sealed record GetMissingDocumentReportQuery(DocumentRequirementStatus? Status, PagedQuery Paging) : IQuery<PagedResult<MissingDocumentReportItemDto>>;

public sealed class GetMissingDocumentReportHandler(
    IEmployeeDocumentRequirementRepository requirements,
    IDocumentTypeDefinitionRepository types,
    IEmployeeContactLookup employeeContacts,
    ITenantContext tenant) : IQueryHandler<GetMissingDocumentReportQuery, PagedResult<MissingDocumentReportItemDto>>
{
    public async Task<Result<PagedResult<MissingDocumentReportItemDto>>> Handle(GetMissingDocumentReportQuery query, CancellationToken cancellationToken)
    {
        var (items, total) = await requirements.ListReportAsync(query.Status, query.Paging.Skip, query.Paging.PageSize, cancellationToken);

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

using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Employee.Application.CustomFields;

/// <summary>Custom field definition catalog (admin); includes inactive definitions only when asked.</summary>
[RequiresPermission(EmployeePermissions.CustomFieldViewKey)]
public sealed record ListCustomFieldDefinitionsQuery(bool IncludeInactive = false) : IQuery<IReadOnlyList<CustomFieldDefinitionDto>>;

public sealed class ListCustomFieldDefinitionsHandler(ICustomFieldDefinitionRepository definitions)
    : IQueryHandler<ListCustomFieldDefinitionsQuery, IReadOnlyList<CustomFieldDefinitionDto>>
{
    public async Task<Result<IReadOnlyList<CustomFieldDefinitionDto>>> Handle(ListCustomFieldDefinitionsQuery query, CancellationToken cancellationToken)
    {
        var list = await definitions.ListAsync(query.IncludeInactive, cancellationToken);
        return Result.Success<IReadOnlyList<CustomFieldDefinitionDto>>(list.Select(CustomFieldDefinitionDto.From).ToList());
    }
}

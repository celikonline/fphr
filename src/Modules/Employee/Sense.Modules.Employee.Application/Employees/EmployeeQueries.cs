using Sense.Modules.Employee.Domain;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Employee.Application.Employees;

/// <summary>Lists minimal employee records for the current tenant (paginated).</summary>
[RequiresPermission("employee.employees.read")]
public sealed record ListEmployeesQuery(PagedQuery Paging) : IQuery<PagedResult<EmployeeListItemDto>>;

public sealed class ListEmployeesHandler(IEmployeeRepository employees) : IQueryHandler<ListEmployeesQuery, PagedResult<EmployeeListItemDto>>
{
    public async Task<Result<PagedResult<EmployeeListItemDto>>> Handle(ListEmployeesQuery query, CancellationToken cancellationToken)
    {
        var total = await employees.CountByTenantAsync(cancellationToken);

        var skip = (query.Paging.Page - 1) * query.Paging.PageSize;
        var list = await employees.ListByTenantAsync(skip, query.Paging.PageSize, cancellationToken);

        var items = list
            .Select(e => new EmployeeListItemDto(e.Id, e.DisplayName, e.Status.ToString(), e.OrgUnitId, e.UserId))
            .ToList();

        return new PagedResult<EmployeeListItemDto>(items, query.Paging.Page, query.Paging.PageSize, total);
    }
}

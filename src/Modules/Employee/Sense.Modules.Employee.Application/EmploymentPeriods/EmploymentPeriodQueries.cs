using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Employee.Application.EmploymentPeriods;

/// <summary>Employment periods and merged seniority of an employee; out-of-scope targets return 404 (IScopedRequest).</summary>
[RequiresPermission(EmployeePermissions.EmployeesReadKey)]
public sealed record GetEmploymentHistoryQuery(Guid EmployeeId) : IQuery<EmploymentHistoryDto>, IScopedRequest
{
    public Guid? TargetEmployeeId => EmployeeId;
}

public sealed class GetEmploymentHistoryHandler(IEmployeeReadStore store, EmploymentHistoryReader reader)
    : IQueryHandler<GetEmploymentHistoryQuery, EmploymentHistoryDto>
{
    public async Task<Result<EmploymentHistoryDto>> Handle(GetEmploymentHistoryQuery query, CancellationToken cancellationToken)
    {
        var employee = await store.GetWithEmploymentPeriodsAsync(query.EmployeeId, cancellationToken);
        return employee is null
            ? Error.NotFound(EmployeeErrors.EmployeeNotFound)
            : Result.Success(await reader.ReadAsync(employee, cancellationToken));
    }
}

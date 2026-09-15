using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Employee.Application.Career;

/// <summary>Career timeline of an employee (newest first); out-of-scope targets return 404 (IScopedRequest).</summary>
[RequiresPermission(EmployeePermissions.EmployeesReadKey)]
public sealed record GetEmployeeCareerQuery(Guid EmployeeId) : IQuery<IReadOnlyList<CareerRecordDto>>, IScopedRequest
{
    public Guid? TargetEmployeeId => EmployeeId;
}

public sealed class GetEmployeeCareerHandler(IEmployeeReadStore store, CareerReader reader)
    : IQueryHandler<GetEmployeeCareerQuery, IReadOnlyList<CareerRecordDto>>
{
    public async Task<Result<IReadOnlyList<CareerRecordDto>>> Handle(GetEmployeeCareerQuery query, CancellationToken cancellationToken)
    {
        var employee = await store.GetWithCareerAsync(query.EmployeeId, cancellationToken);
        return employee is null
            ? Error.NotFound(EmployeeErrors.EmployeeNotFound)
            : Result.Success(await reader.ReadAsync(employee, cancellationToken));
    }
}

/// <summary>The caller's own career timeline ("Kendi kartım", read-only); 404 when the caller has no linked employee record.</summary>
[RequiresPermission(EmployeePermissions.MyViewKey)]
public sealed record GetMyCareerQuery : IQuery<IReadOnlyList<CareerRecordDto>>;

public sealed class GetMyCareerHandler(ICurrentUser currentUser, IEmployeeReadStore store, CareerReader reader)
    : IQueryHandler<GetMyCareerQuery, IReadOnlyList<CareerRecordDto>>
{
    public async Task<Result<IReadOnlyList<CareerRecordDto>>> Handle(GetMyCareerQuery query, CancellationToken cancellationToken)
    {
        // Same fallback as GetMyEmployeeHandler: the eid claim can lag behind a link made after sign-in.
        var row = currentUser.EmployeeId is { } employeeId ? await store.GetAsync(employeeId, cancellationToken) : null;
        if (row is null && currentUser.UserId is { } userId)
        {
            row = await store.GetByUserIdAsync(userId, cancellationToken);
        }

        if (row is null || (row.UserId is { } linkedUserId && linkedUserId != currentUser.UserId))
        {
            return Error.NotFound(EmployeeErrors.EmployeeNotFound);
        }

        var employee = await store.GetWithCareerAsync(row.Id, cancellationToken);
        return employee is null
            ? Error.NotFound(EmployeeErrors.EmployeeNotFound)
            : Result.Success(await reader.ReadAsync(employee, cancellationToken));
    }
}

using FluentValidation;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Leave.Application.Balances;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Balances;
using Sense.Modules.Leave.Domain.Calendar;
using Sense.Modules.Leave.Domain.Ledger;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Workflow.Contracts;
using Sense.Modules.Workflow.Contracts.Events;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Leave.Application.Requests;

[RequiresPermission("leave.my.requests")]
public sealed record ListMyLeaveRequestsQuery(int Take = PagingDefaults.MaxPageSize) : IQuery<IReadOnlyList<LeaveRequestDto>>;

public sealed class ListMyLeaveRequestsHandler(
    ICurrentUser currentUser, ILeaveRequestRepository requests, IEmployeeLeaveProfileRepository profiles, ILeaveRuleSetRepository ruleSets)
    : IQueryHandler<ListMyLeaveRequestsQuery, IReadOnlyList<LeaveRequestDto>>
{
    public async Task<Result<IReadOnlyList<LeaveRequestDto>>> Handle(ListMyLeaveRequestsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(LeaveErrors.NoEmployeeForUser);
        }

        var list = await LeaveRequestMapper.ToDtosAsync(employeeId, await requests.ListByEmployeeAsync(employeeId, query.Take, cancellationToken), profiles, ruleSets, cancellationToken);
        return Result.Success(list);
    }
}

[RequiresPermission("leave.requests.read")]
public sealed record ListEmployeeLeaveRequestsQuery(Guid EmployeeId, int Take = PagingDefaults.MaxPageSize) : IQuery<IReadOnlyList<LeaveRequestDto>>;

public sealed class ListEmployeeLeaveRequestsHandler(
    ILeaveRequestRepository requests, IDataScopeService dataScope, IEmployeeLeaveProfileRepository profiles, ILeaveRuleSetRepository ruleSets)
    : IQueryHandler<ListEmployeeLeaveRequestsQuery, IReadOnlyList<LeaveRequestDto>>
{
    public async Task<Result<IReadOnlyList<LeaveRequestDto>>> Handle(ListEmployeeLeaveRequestsQuery query, CancellationToken cancellationToken)
    {
        if (!await dataScope.CanAccessEmployeeAsync(LeavePermissions.RequestsRead.Key, query.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(LeaveErrors.EmployeeNotInScope);
        }

        var list = await LeaveRequestMapper.ToDtosAsync(
            query.EmployeeId, await requests.ListByEmployeeAsync(query.EmployeeId, query.Take, cancellationToken), profiles, ruleSets, cancellationToken);
        return Result.Success(list);
    }
}

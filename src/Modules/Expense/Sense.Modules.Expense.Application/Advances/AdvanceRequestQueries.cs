using Sense.Modules.Expense.Domain;
using Sense.Modules.Expense.Domain.Advances;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Expense.Application.Advances;

[RequiresPermission("expense.my.advances")]
public sealed record ListMyAdvanceRequestsQuery : IQuery<IReadOnlyList<AdvanceRequestDto>>;

public sealed class ListMyAdvanceRequestsHandler(ICurrentUser currentUser, IAdvanceRequestRepository requests)
    : IQueryHandler<ListMyAdvanceRequestsQuery, IReadOnlyList<AdvanceRequestDto>>
{
    public async Task<Result<IReadOnlyList<AdvanceRequestDto>>> Handle(ListMyAdvanceRequestsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(ExpenseErrors.NoEmployeeForUser);
        }

        var list = await requests.ListByEmployeeAsync(employeeId, cancellationToken);
        IReadOnlyList<AdvanceRequestDto> dtos = list.Select(AdvanceRequestMapper.ToDto).ToList();
        return Result.Success(dtos);
    }
}

/// <summary>HR/Finance worklist: tenant-wide advance requests, optionally filtered to one status (e.g. Approved, awaiting pay).</summary>
[RequiresPermission("expense.advances.view")]
public sealed record ListAdvanceRequestsQuery(AdvanceRequestStatus? Status) : IQuery<IReadOnlyList<AdvanceRequestDto>>;

public sealed class ListAdvanceRequestsHandler(IAdvanceRequestRepository requests) : IQueryHandler<ListAdvanceRequestsQuery, IReadOnlyList<AdvanceRequestDto>>
{
    public async Task<Result<IReadOnlyList<AdvanceRequestDto>>> Handle(ListAdvanceRequestsQuery query, CancellationToken cancellationToken)
    {
        var list = await requests.ListAsync(query.Status, cancellationToken);
        IReadOnlyList<AdvanceRequestDto> dtos = list.Select(AdvanceRequestMapper.ToDto).ToList();
        return Result.Success(dtos);
    }
}

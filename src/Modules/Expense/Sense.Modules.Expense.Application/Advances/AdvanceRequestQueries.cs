using Sense.Modules.Expense.Domain;
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

using Sense.Modules.Expense.Domain;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Expense.Application.ExpenseClaims;

[RequiresPermission("expense.my.claims")]
public sealed record ListMyExpenseClaimsQuery : IQuery<IReadOnlyList<ExpenseClaimDto>>;

public sealed class ListMyExpenseClaimsHandler(ICurrentUser currentUser, IExpenseClaimRepository claims)
    : IQueryHandler<ListMyExpenseClaimsQuery, IReadOnlyList<ExpenseClaimDto>>
{
    public async Task<Result<IReadOnlyList<ExpenseClaimDto>>> Handle(ListMyExpenseClaimsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(ExpenseErrors.NoEmployeeForUser);
        }

        var list = await claims.ListByEmployeeAsync(employeeId, cancellationToken);
        IReadOnlyList<ExpenseClaimDto> dtos = list.Select(ExpenseClaimMapper.ToDto).ToList();
        return Result.Success(dtos);
    }
}

[RequiresPermission("expense.my.claims")]
public sealed record GetExpenseClaimQuery(Guid ClaimId) : IQuery<ExpenseClaimDto>;

public sealed class GetExpenseClaimHandler(ICurrentUser currentUser, IExpenseClaimRepository claims)
    : IQueryHandler<GetExpenseClaimQuery, ExpenseClaimDto>
{
    public async Task<Result<ExpenseClaimDto>> Handle(GetExpenseClaimQuery query, CancellationToken cancellationToken)
    {
        var existing = await claims.GetByIdAsync(query.ClaimId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        if (existing.Value.EmployeeId != currentUser.EmployeeId)
        {
            return Error.Forbidden(ExpenseErrors.NotOwner);
        }

        return ExpenseClaimMapper.ToDto(existing.Value);
    }
}

using Sense.Modules.Expense.Domain;
using Sense.Modules.Expense.Domain.ExpenseClaims;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Expense.Application.ExpenseClaims;

[RequiresPermission("expense.my.claims")]
public sealed record ListMyExpenseClaimsQuery : IQuery<IReadOnlyList<ExpenseClaimDto>>;

public sealed class ListMyExpenseClaimsHandler(ICurrentUser currentUser, IExpenseClaimRepository claims, IExpenseCategoryRepository categories)
    : IQueryHandler<ListMyExpenseClaimsQuery, IReadOnlyList<ExpenseClaimDto>>
{
    public async Task<Result<IReadOnlyList<ExpenseClaimDto>>> Handle(ListMyExpenseClaimsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(ExpenseErrors.NoEmployeeForUser);
        }

        var list = await claims.ListByEmployeeAsync(employeeId, cancellationToken);
        var categoryLimits = await ExpenseCategoryLimitsHelper.CategoryLimitsAsync(categories, cancellationToken);
        IReadOnlyList<ExpenseClaimDto> dtos = list.Select(c => ExpenseClaimMapper.ToDto(c, categoryLimits)).ToList();
        return Result.Success(dtos);
    }
}

[RequiresPermission("expense.my.claims")]
public sealed record GetExpenseClaimQuery(Guid ClaimId) : IQuery<ExpenseClaimDto>;

public sealed class GetExpenseClaimHandler(ICurrentUser currentUser, IExpenseClaimRepository claims, IExpenseCategoryRepository categories)
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

        var categoryLimits = await ExpenseCategoryLimitsHelper.CategoryLimitsAsync(categories, cancellationToken);
        return ExpenseClaimMapper.ToDto(existing.Value, categoryLimits);
    }
}

/// <summary>HR/Finance worklist: tenant-wide non-Draft claims, optionally filtered to one status (e.g. Approved, awaiting pay).</summary>
[RequiresPermission("expense.claims.view")]
public sealed record ListExpenseClaimsQuery(ExpenseClaimStatus? Status) : IQuery<IReadOnlyList<ExpenseClaimDto>>;

public sealed class ListExpenseClaimsHandler(IExpenseClaimRepository claims, IExpenseCategoryRepository categories)
    : IQueryHandler<ListExpenseClaimsQuery, IReadOnlyList<ExpenseClaimDto>>
{
    public async Task<Result<IReadOnlyList<ExpenseClaimDto>>> Handle(ListExpenseClaimsQuery query, CancellationToken cancellationToken)
    {
        var list = await claims.ListAsync(query.Status, cancellationToken);
        var categoryLimits = await ExpenseCategoryLimitsHelper.CategoryLimitsAsync(categories, cancellationToken);
        IReadOnlyList<ExpenseClaimDto> dtos = list.Select(c => ExpenseClaimMapper.ToDto(c, categoryLimits)).ToList();
        return Result.Success(dtos);
    }
}

/// <summary>Shared helper (S-9's ExceedsCategoryLimit): categoryId -> MaxAmount, including inactive categories since
/// older claim items may still reference a since-deactivated category.</summary>
internal static class ExpenseCategoryLimitsHelper
{
    public static async Task<IReadOnlyDictionary<Guid, decimal?>> CategoryLimitsAsync(IExpenseCategoryRepository categories, CancellationToken cancellationToken)
    {
        var all = await categories.ListAsync(includeInactive: true, cancellationToken);
        return all.ToDictionary(c => c.Id, c => c.MaxAmount);
    }
}

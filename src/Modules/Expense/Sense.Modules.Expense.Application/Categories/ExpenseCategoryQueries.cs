using Sense.Modules.Expense.Domain;
using Sense.Modules.Expense.Domain.ExpenseClaims;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Expense.Application.Categories;

/// <summary>Lists the tenant's expense categories, lazily seeding the default catalog on first call.</summary>
[RequiresPermission("expense.categories.view")]
public sealed record ListExpenseCategoriesQuery(bool IncludeInactive) : IQuery<IReadOnlyList<ExpenseCategoryDto>>;

public sealed class ListExpenseCategoriesHandler(
    ITenantContext tenant, IExpenseCategoryRepository categories, ExpenseCategoryProvisioningService provisioning, IExpenseUnitOfWork unitOfWork)
    : IQueryHandler<ListExpenseCategoriesQuery, IReadOnlyList<ExpenseCategoryDto>>
{
    public async Task<Result<IReadOnlyList<ExpenseCategoryDto>>> Handle(ListExpenseCategoriesQuery query, CancellationToken cancellationToken)
    {
        await provisioning.EnsureSeededAsync(tenant.TenantId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var list = await categories.ListAsync(query.IncludeInactive, cancellationToken);
        IReadOnlyList<ExpenseCategoryDto> dtos = list.Select(ToDto).ToList();
        return Result.Success(dtos);
    }

    internal static ExpenseCategoryDto ToDto(ExpenseCategory category) =>
        new(category.Id, category.Code, category.NameKey, category.DefaultTaxRate, category.IsActive);
}

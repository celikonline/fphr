using FluentValidation;
using Sense.Modules.Expense.Domain;
using Sense.Modules.Expense.Domain.ExpenseClaims;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Expense.Application.Categories;

[RequiresPermission("expense.categories.manage")]
public sealed record CreateExpenseCategoryCommand(string Code, string NameKey, decimal? DefaultTaxRate) : ICommand<Guid>;

public sealed class CreateExpenseCategoryValidator : AbstractValidator<CreateExpenseCategoryCommand>
{
    public CreateExpenseCategoryValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(ExpenseLimits.CategoryCodeMaxLength);
        RuleFor(x => x.NameKey).NotEmpty().MaximumLength(ExpenseLimits.CategoryNameKeyMaxLength);
        RuleFor(x => x.DefaultTaxRate).InclusiveBetween(0, 100).When(x => x.DefaultTaxRate is not null);
    }
}

public sealed class CreateExpenseCategoryHandler(ITenantContext tenant, IExpenseCategoryRepository categories)
    : ICommandHandler<CreateExpenseCategoryCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateExpenseCategoryCommand command, CancellationToken cancellationToken)
    {
        var created = ExpenseCategory.Create(tenant.TenantId, command.Code, command.NameKey, command.DefaultTaxRate);
        if (created.IsFailure)
        {
            return created.Error;
        }

        categories.Add(created.Value);
        return created.Value.Id;
    }
}

[RequiresPermission("expense.categories.manage")]
public sealed record SetExpenseCategoryActiveCommand(Guid CategoryId, bool IsActive) : ICommand;

public sealed class SetExpenseCategoryActiveHandler(IExpenseCategoryRepository categories) : ICommandHandler<SetExpenseCategoryActiveCommand>
{
    public async Task<Result> Handle(SetExpenseCategoryActiveCommand command, CancellationToken cancellationToken)
    {
        var existing = await categories.GetByIdAsync(command.CategoryId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        if (command.IsActive)
        {
            existing.Value.Activate();
            return Result.Success();
        }

        var deactivated = existing.Value.Deactivate();
        return deactivated.IsFailure ? deactivated.Error : Result.Success();
    }
}

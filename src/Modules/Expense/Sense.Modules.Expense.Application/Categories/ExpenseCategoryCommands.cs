using FluentValidation;
using Sense.Modules.Expense.Domain;
using Sense.Modules.Expense.Domain.ExpenseClaims;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Expense.Application.Categories;

[RequiresPermission("expense.categories.manage")]
public sealed record CreateExpenseCategoryCommand(string Code, string NameKey, decimal? DefaultTaxRate, decimal? MaxAmount = null) : ICommand<Guid>;

public sealed class CreateExpenseCategoryValidator : AbstractValidator<CreateExpenseCategoryCommand>
{
    public CreateExpenseCategoryValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(ExpenseLimits.CategoryCodeMaxLength);
        RuleFor(x => x.NameKey).NotEmpty().MaximumLength(ExpenseLimits.CategoryNameKeyMaxLength);
        RuleFor(x => x.DefaultTaxRate).InclusiveBetween(0, 100).When(x => x.DefaultTaxRate is not null);
        RuleFor(x => x.MaxAmount).GreaterThanOrEqualTo(0).When(x => x.MaxAmount is not null);
    }
}

public sealed class CreateExpenseCategoryHandler(ITenantContext tenant, IExpenseCategoryRepository categories)
    : ICommandHandler<CreateExpenseCategoryCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateExpenseCategoryCommand command, CancellationToken cancellationToken)
    {
        var created = ExpenseCategory.Create(tenant.TenantId, command.Code, command.NameKey, command.DefaultTaxRate, maxAmount: command.MaxAmount);
        if (created.IsFailure)
        {
            return created.Error;
        }

        categories.Add(created.Value);
        return created.Value.Id;
    }
}

/// <summary>PUT /expense/categories/{id} (S-9): renames the category and/or sets its spending policy ceiling. The
/// seeded system catalog (<see cref="ExpenseCategorySeed.SystemDefinitions"/>) may have MaxAmount edited but not
/// NameKey - see <see cref="ExpenseErrors.CategorySystemImmutable"/>.</summary>
[RequiresPermission("expense.categories.manage")]
public sealed record UpdateExpenseCategoryCommand(Guid CategoryId, string NameKey, decimal? MaxAmount) : ICommand;

public sealed class UpdateExpenseCategoryValidator : AbstractValidator<UpdateExpenseCategoryCommand>
{
    public UpdateExpenseCategoryValidator()
    {
        RuleFor(x => x.NameKey).NotEmpty().MaximumLength(ExpenseLimits.CategoryNameKeyMaxLength);
        RuleFor(x => x.MaxAmount).GreaterThanOrEqualTo(0).When(x => x.MaxAmount is not null);
    }
}

public sealed class UpdateExpenseCategoryHandler(IExpenseCategoryRepository categories) : ICommandHandler<UpdateExpenseCategoryCommand>
{
    private static readonly HashSet<string> SystemCategoryCodes = ExpenseCategorySeed.SystemDefinitions()
        .Select(d => d.Code)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public async Task<Result> Handle(UpdateExpenseCategoryCommand command, CancellationToken cancellationToken)
    {
        var existing = await categories.GetByIdAsync(command.CategoryId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var category = existing.Value;
        var isSystemCategory = SystemCategoryCodes.Contains(category.Code);
        if (isSystemCategory && !string.Equals(category.NameKey, command.NameKey, StringComparison.Ordinal))
        {
            return Error.Conflict(ExpenseErrors.CategorySystemImmutable);
        }

        var renamed = category.Rename(command.NameKey);
        if (renamed.IsFailure)
        {
            return renamed;
        }

        category.SetMaxAmount(command.MaxAmount);
        return Result.Success();
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

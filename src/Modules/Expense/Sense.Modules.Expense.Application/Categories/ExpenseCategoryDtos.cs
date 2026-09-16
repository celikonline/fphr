namespace Sense.Modules.Expense.Application.Categories;

public sealed record ExpenseCategoryDto(Guid Id, string Code, string NameKey, decimal? DefaultTaxRate, bool IsActive, decimal? MaxAmount = null);

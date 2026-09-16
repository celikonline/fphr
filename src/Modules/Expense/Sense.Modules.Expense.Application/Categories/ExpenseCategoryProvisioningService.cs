using Sense.Modules.Expense.Domain;
using Sense.Modules.Expense.Domain.ExpenseClaims;

namespace Sense.Modules.Expense.Application.Categories;

/// <summary>Default seed row: code + localization resource key + a pre-selected VAT rate (%, tenant may still edit).</summary>
public sealed record ExpenseCategorySeedRow(string Code, string NameKey, decimal? DefaultTaxRate);

/// <summary>System-wide starting catalog (spec: "expense.category.travel"/"expense.category.food"; tenants may add/deactivate their own).</summary>
public static class ExpenseCategorySeed
{
    public static IReadOnlyList<ExpenseCategorySeedRow> SystemDefinitions() =>
    [
        new("travel", "expense.category.travel", null),
        new("food", "expense.category.food", null),
    ];
}

/// <summary>
/// Seeds the tenant's category catalog the first time it's needed - mirrors Documents' DocumentTypeProvisioningService
/// (no per-module tenant-provisioning hook exists yet, so this runs lazily from the first read/write that needs the
/// catalog). Does not save; the caller must call its own IExpenseUnitOfWork.SaveChangesAsync afterwards.
/// </summary>
public sealed class ExpenseCategoryProvisioningService(IExpenseCategoryRepository categories)
{
    public async Task EnsureSeededAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (await categories.AnyAsync(cancellationToken))
        {
            return;
        }

        foreach (var row in ExpenseCategorySeed.SystemDefinitions())
        {
            categories.Add(ExpenseCategory.Create(tenantId, row.Code, row.NameKey, row.DefaultTaxRate).Value);
        }
    }
}

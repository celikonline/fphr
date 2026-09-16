using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Expense.Application;
using Sense.Modules.Expense.Application.Categories;
using Sense.Modules.Expense.Contracts;
using Sense.Modules.Expense.Domain;
using Sense.Modules.Expense.Infrastructure.Lookups;
using Sense.Modules.Expense.Infrastructure.Persistence;

namespace Sense.Modules.Expense.Infrastructure;

public static class ExpenseServiceCollectionExtensions
{
    public static IServiceCollection AddExpenseCoreServices(this IServiceCollection services)
    {
        services.AddScoped<IExpenseCategoryRepository, ExpenseCategoryRepository>();
        services.AddScoped<IExpenseClaimRepository, ExpenseClaimRepository>();
        services.AddScoped<IAdvanceRequestRepository, AdvanceRequestRepository>();
        services.AddScoped<IExpenseUnitOfWork>(sp => sp.GetRequiredService<ExpenseDbContext>());
        services.AddScoped<ExpenseCategoryProvisioningService>();
        services.AddScoped<IApprovedExpensePayoutReader, ApprovedExpensePayoutReader>();
        return services;
    }
}

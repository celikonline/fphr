using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Expense.Application;
using Sense.Modules.Expense.Application.Advances;
using Sense.Modules.Expense.Application.Categories;
using Sense.Modules.Expense.Application.ExpenseClaims;
using Sense.Modules.Expense.Contracts;
using Sense.Modules.Expense.Domain;
using Sense.Modules.Expense.Infrastructure.Lookups;
using Sense.Modules.Expense.Infrastructure.Persistence;
using Sense.Modules.Workflow.Contracts.Events;
using Sense.Shared.Contracts.Events;

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

    /// <summary>
    /// Expense's Workflow decision handlers (ApprovalCompleted/ApprovalRejected for "ExpenseClaim" and "Advance"),
    /// registered explicitly for Sense.Worker - it does not scan the whole Application assembly (API-only handler
    /// dependencies stay out), so without this an approved claim/advance never leaves Submitted.
    /// </summary>
    public static IServiceCollection AddExpenseIntegrationEventHandlers(this IServiceCollection services)
    {
        services.AddScoped<IIntegrationEventHandler<ApprovalCompleted>, ExpenseClaimApprovalCompletedHandler>();
        services.AddScoped<IIntegrationEventHandler<ApprovalRejected>, ExpenseClaimApprovalRejectedHandler>();
        services.AddScoped<IIntegrationEventHandler<ApprovalCompleted>, AdvanceApprovalCompletedHandler>();
        services.AddScoped<IIntegrationEventHandler<ApprovalRejected>, AdvanceApprovalRejectedHandler>();
        return services;
    }
}

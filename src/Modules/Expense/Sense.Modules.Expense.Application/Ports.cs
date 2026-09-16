using Sense.Shared.Contracts.Persistence;

namespace Sense.Modules.Expense.Application;

/// <summary>Expense module's UnitOfWork surface for the command pipeline; implemented by ExpenseDbContext.</summary>
public interface IExpenseUnitOfWork : IUnitOfWork;

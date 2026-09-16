using Sense.Modules.Expense.Domain.Advances;
using Sense.Modules.Expense.Domain.ExpenseClaims;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Expense.Domain;

/// <summary>ExpenseCategory repository.</summary>
public interface IExpenseCategoryRepository
{
    void Add(ExpenseCategory category);

    Task<Result<ExpenseCategory>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ExpenseCategory?> FindByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExpenseCategory>> ListAsync(bool includeInactive = false, CancellationToken cancellationToken = default);

    Task<bool> AnyAsync(CancellationToken cancellationToken = default);
}

/// <summary>ExpenseClaim aggregate repository.</summary>
public interface IExpenseClaimRepository
{
    void Add(ExpenseClaim claim);

    Task<Result<ExpenseClaim>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ExpenseClaim?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExpenseClaim>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>Non-Draft claims for the payments-overview projection (Expense-schema-only, per design doc).</summary>
    Task<IReadOnlyList<ExpenseClaim>> ListSubmittedByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);
}

/// <summary>AdvanceRequest aggregate repository.</summary>
public interface IAdvanceRequestRepository
{
    void Add(AdvanceRequest request);

    Task<Result<AdvanceRequest>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<AdvanceRequest?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdvanceRequest>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);
}

using Microsoft.EntityFrameworkCore;
using Sense.Modules.Expense.Domain;
using Sense.Modules.Expense.Domain.Advances;
using Sense.Modules.Expense.Domain.ExpenseClaims;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Expense.Infrastructure.Persistence;

public sealed class ExpenseCategoryRepository(ExpenseDbContext context) : IExpenseCategoryRepository
{
    public void Add(ExpenseCategory category) => context.ExpenseCategories.Add(category);

    public async Task<Result<ExpenseCategory>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var category = await context.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        return category is null ? Error.NotFound(ExpenseDomainErrors.CategoryNotFound) : category;
    }

    public async Task<ExpenseCategory?> FindByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        await context.ExpenseCategories.FirstOrDefaultAsync(c => c.Code == code, cancellationToken);

    public async Task<IReadOnlyList<ExpenseCategory>> ListAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var query = context.ExpenseCategories.AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        return await query.OrderBy(c => c.NameKey).ToListAsync(cancellationToken);
    }

    public async Task<bool> AnyAsync(CancellationToken cancellationToken = default) =>
        await context.ExpenseCategories.AnyAsync(cancellationToken);
}

public sealed class ExpenseClaimRepository(ExpenseDbContext context) : IExpenseClaimRepository
{
    public void Add(ExpenseClaim claim) => context.ExpenseClaims.Add(claim);

    public async Task<Result<ExpenseClaim>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var claim = await context.ExpenseClaims.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        return claim is null ? Error.NotFound(ExpenseDomainErrors.ClaimNotFound) : claim;
    }

    public async Task<ExpenseClaim?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.ExpenseClaims.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ExpenseClaim>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        await context.ExpenseClaims
            .Where(c => c.EmployeeId == employeeId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ExpenseClaim>> ListSubmittedByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        await context.ExpenseClaims
            .Where(c => c.EmployeeId == employeeId && c.Status != ExpenseClaimStatus.Draft)
            .OrderByDescending(c => c.SubmittedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ExpenseClaim>> ListAsync(ExpenseClaimStatus? status, CancellationToken cancellationToken = default)
    {
        var query = context.ExpenseClaims.Where(c => c.Status != ExpenseClaimStatus.Draft);
        if (status is { } value)
        {
            query = query.Where(c => c.Status == value);
        }

        return await query.OrderByDescending(c => c.SubmittedAt).ToListAsync(cancellationToken);
    }
}

public sealed class AdvanceRequestRepository(ExpenseDbContext context) : IAdvanceRequestRepository
{
    public void Add(AdvanceRequest request) => context.AdvanceRequests.Add(request);

    public async Task<Result<AdvanceRequest>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var request = await context.AdvanceRequests.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        return request is null ? Error.NotFound(ExpenseDomainErrors.AdvanceNotFound) : request;
    }

    public async Task<AdvanceRequest?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.AdvanceRequests.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<AdvanceRequest>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        await context.AdvanceRequests
            .Where(a => a.EmployeeId == employeeId)
            .OrderByDescending(a => a.RequestedDate)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AdvanceRequest>> ListAsync(AdvanceRequestStatus? status, CancellationToken cancellationToken = default)
    {
        var query = context.AdvanceRequests.AsQueryable();
        if (status is { } value)
        {
            query = query.Where(a => a.Status == value);
        }

        return await query.OrderByDescending(a => a.RequestedDate).ToListAsync(cancellationToken);
    }
}

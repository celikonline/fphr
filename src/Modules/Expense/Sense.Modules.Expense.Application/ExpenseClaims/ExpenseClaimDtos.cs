using Sense.Modules.Expense.Domain.ExpenseClaims;

namespace Sense.Modules.Expense.Application.ExpenseClaims;

public sealed record ExpenseItemDto(
    Guid Id, Guid CategoryId, decimal Amount, DateOnly ReceiptDate, decimal? TaxRate, decimal TaxAmount,
    string Currency, string? ReceiptFileId, string? Description, bool ExceedsCategoryLimit = false);

public sealed record ExpenseClaimDto(
    Guid Id, Guid EmployeeId, string? Description, decimal TotalAmount, ExpenseClaimStatus Status,
    Guid? ApprovalRequestId, DateTime? SubmittedAt, DateTime? DecidedAt, DateTime? PaidAt, Guid? PaidByEmployeeId,
    string? PaymentNote, IReadOnlyList<ExpenseItemDto> Items);

internal static class ExpenseClaimMapper
{
    /// <summary><paramref name="categoryLimits"/>: categoryId -> MaxAmount (S-9's ExceedsCategoryLimit, a warning
    /// only - it never blocks submission, see ExpenseCategory.MaxAmount doc). Missing/null entries mean "no limit".</summary>
    public static ExpenseClaimDto ToDto(ExpenseClaim claim, IReadOnlyDictionary<Guid, decimal?> categoryLimits) =>
        new(
            claim.Id, claim.EmployeeId, claim.Description, claim.TotalAmount, claim.Status,
            claim.ApprovalRequestId, claim.SubmittedAt, claim.DecidedAt, claim.PaidAt, claim.PaidByEmployeeId,
            claim.PaymentNote, claim.Items.Select(item => ToDto(item, categoryLimits)).ToList());

    public static ExpenseItemDto ToDto(ExpenseItem item, IReadOnlyDictionary<Guid, decimal?> categoryLimits)
    {
        var exceedsLimit = categoryLimits.TryGetValue(item.CategoryId, out var maxAmount) && maxAmount is { } limit && item.Amount > limit;
        return new(item.Id, item.CategoryId, item.Amount, item.ReceiptDate, item.TaxRate, item.TaxAmount, item.Currency, item.ReceiptFileId, item.Description, exceedsLimit);
    }
}

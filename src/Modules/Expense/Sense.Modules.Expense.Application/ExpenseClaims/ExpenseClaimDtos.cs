using Sense.Modules.Expense.Domain.ExpenseClaims;

namespace Sense.Modules.Expense.Application.ExpenseClaims;

public sealed record ExpenseItemDto(
    Guid Id, Guid CategoryId, decimal Amount, DateOnly ReceiptDate, decimal? TaxRate, decimal TaxAmount,
    string Currency, string? ReceiptFileId, string? Description);

public sealed record ExpenseClaimDto(
    Guid Id, Guid EmployeeId, string? Description, decimal TotalAmount, ExpenseClaimStatus Status,
    Guid? ApprovalRequestId, DateTime? SubmittedAt, DateTime? DecidedAt, DateTime? PaidAt, IReadOnlyList<ExpenseItemDto> Items);

internal static class ExpenseClaimMapper
{
    public static ExpenseClaimDto ToDto(ExpenseClaim claim) =>
        new(
            claim.Id, claim.EmployeeId, claim.Description, claim.TotalAmount, claim.Status,
            claim.ApprovalRequestId, claim.SubmittedAt, claim.DecidedAt, claim.PaidAt,
            claim.Items.Select(ToDto).ToList());

    public static ExpenseItemDto ToDto(ExpenseItem item) =>
        new(item.Id, item.CategoryId, item.Amount, item.ReceiptDate, item.TaxRate, item.TaxAmount, item.Currency, item.ReceiptFileId, item.Description);
}

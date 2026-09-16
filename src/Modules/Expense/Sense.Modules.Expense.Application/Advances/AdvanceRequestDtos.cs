using Sense.Modules.Expense.Domain.Advances;

namespace Sense.Modules.Expense.Application.Advances;

public sealed record AdvanceRequestDto(
    Guid Id, Guid EmployeeId, decimal Amount, string Currency, DateOnly RequestedDate, bool IsInstallmentRequested,
    int? InstallmentCount, string? Description, AdvanceRequestStatus Status, Guid? ApprovalRequestId, DateTime? DecidedAt,
    DateTime? PaidAt, Guid? PaidByEmployeeId, string? PaymentNote);

internal static class AdvanceRequestMapper
{
    public static AdvanceRequestDto ToDto(AdvanceRequest request) =>
        new(
            request.Id, request.EmployeeId, request.Amount, request.Currency, request.RequestedDate, request.IsInstallmentRequested,
            request.InstallmentCount, request.Description, request.Status, request.ApprovalRequestId, request.DecidedAt,
            request.PaidAt, request.PaidByEmployeeId, request.PaymentNote);
}

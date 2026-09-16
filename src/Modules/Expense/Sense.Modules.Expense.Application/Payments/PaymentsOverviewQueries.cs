using Sense.Modules.Expense.Domain;
using Sense.Modules.Expense.Domain.ExpenseClaims;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Expense.Application.Payments;

/// <summary>
/// "Kendi hesabım > Ödemeler": combined expense-claim + advance-request status list. Per the design doc this
/// projection reads only the Expense schema (no cross-module joins) - Draft claims are excluded since they were
/// never sent anywhere.
/// </summary>
[RequiresPermission("expense.my.payments")]
public sealed record GetMyPaymentsOverviewQuery : IQuery<IReadOnlyList<PaymentOverviewRow>>;

public sealed class GetMyPaymentsOverviewHandler(ICurrentUser currentUser, IExpenseClaimRepository claims, IAdvanceRequestRepository advances)
    : IQueryHandler<GetMyPaymentsOverviewQuery, IReadOnlyList<PaymentOverviewRow>>
{
    public async Task<Result<IReadOnlyList<PaymentOverviewRow>>> Handle(GetMyPaymentsOverviewQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(ExpenseErrors.NoEmployeeForUser);
        }

        var claimRows = (await claims.ListSubmittedByEmployeeAsync(employeeId, cancellationToken))
            .Select(c => new PaymentOverviewRow(
                PaymentKind.Expense,
                DateOnly.FromDateTime(c.SubmittedAt ?? c.CreatedAt),
                c.TotalAmount,
                c.Items.Count > 0 ? c.Items[0].Currency : string.Empty,
                c.Status.ToString(),
                c.Id,
                c.Description));

        var advanceRows = (await advances.ListByEmployeeAsync(employeeId, cancellationToken))
            .Select(a => new PaymentOverviewRow(PaymentKind.Advance, a.RequestedDate, a.Amount, a.Currency, a.Status.ToString(), a.Id, a.Description));

        IReadOnlyList<PaymentOverviewRow> rows = claimRows.Concat(advanceRows).OrderByDescending(r => r.Date).ToList();
        return Result.Success(rows);
    }
}

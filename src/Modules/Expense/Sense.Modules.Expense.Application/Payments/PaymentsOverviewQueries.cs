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

/// <summary>
/// Çalışan kartı "Ödemeler" sekmesi: HR/Finance için tek bir çalışanın aynı projeksiyonu (11.3.8 tasarım kararı -
/// yalnız Expense şemasını okur). Hem Harcama hem Avans görünürlüğü gerektiği için iki izin de aranır (bugün
/// yalnız HR/Finance/Owner ikisine birden sahip; Manager yalnız "expense.claims.view" taşıdığından bu kartı göremez -
/// bilinçli, ayrı bir "expense.payment.view" izni tanımlanana kadar).
/// </summary>
[RequiresPermission("expense.claims.view")]
[RequiresPermission("expense.advances.view")]
public sealed record GetEmployeePaymentsOverviewQuery(Guid EmployeeId) : IQuery<IReadOnlyList<PaymentOverviewRow>>, IScopedRequest
{
    public Guid? TargetEmployeeId => EmployeeId;
}

public sealed class GetEmployeePaymentsOverviewHandler(IExpenseClaimRepository claims, IAdvanceRequestRepository advances)
    : IQueryHandler<GetEmployeePaymentsOverviewQuery, IReadOnlyList<PaymentOverviewRow>>
{
    public async Task<Result<IReadOnlyList<PaymentOverviewRow>>> Handle(GetEmployeePaymentsOverviewQuery query, CancellationToken cancellationToken)
    {
        var claimRows = (await claims.ListSubmittedByEmployeeAsync(query.EmployeeId, cancellationToken))
            .Select(c => new PaymentOverviewRow(
                PaymentKind.Expense,
                DateOnly.FromDateTime(c.SubmittedAt ?? c.CreatedAt),
                c.TotalAmount,
                c.Items.Count > 0 ? c.Items[0].Currency : string.Empty,
                c.Status.ToString(),
                c.Id,
                c.Description));

        var advanceRows = (await advances.ListByEmployeeAsync(query.EmployeeId, cancellationToken))
            .Select(a => new PaymentOverviewRow(PaymentKind.Advance, a.RequestedDate, a.Amount, a.Currency, a.Status.ToString(), a.Id, a.Description));

        IReadOnlyList<PaymentOverviewRow> rows = claimRows.Concat(advanceRows).OrderByDescending(r => r.Date).ToList();
        return Result.Success(rows);
    }
}

using Microsoft.Extensions.Localization;
using Sense.Modules.Employee.Contracts.CardPdf;
using Sense.Modules.Expense.Application.Payments;
using Sense.Modules.Expense.Domain.Advances;
using Sense.Modules.Expense.Domain.ExpenseClaims;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.Export;
using static Sense.Modules.Employee.Contracts.CardPdf.EmployeeCardPdfFormat;

namespace Sense.Modules.Expense.Infrastructure.CardPdf;

/// <summary>Card PDF "Ödemeler" (after Employee's salary/bank blocks): expense/advance payments within the period.</summary>
public sealed class ExpenseCardPdfSection(IDispatcher dispatcher, IStringLocalizer<SharedResource> localizer) : IEmployeeCardPdfSection
{
    public string Key => EmployeeCardPdfSectionKeys.Payments;

    public int Order => 10;

    public string? ModuleKey => ProductModuleKeys.Expense;

    public async Task<EmployeeCardPdfSectionResult> BuildAsync(EmployeeCardPdfRequest request, CancellationToken cancellationToken)
    {
        var result = request.IsOwnCard
            ? await dispatcher.Query(new GetMyPaymentsOverviewQuery(), cancellationToken)
            : await dispatcher.Query(new GetEmployeePaymentsOverviewQuery(request.EmployeeId), cancellationToken);
        return EmployeeCardPdfSectionResult.Combine(EmployeeCardPdfSectionResult.Part(result, rows => Table(
            "Masraf ve avans ödemeleri",
            ["Tarih", "Tür", "Tutar", "Durum", "Açıklama"],
            rows.Where(r => InPeriod(r.Date, null, request.From, request.To))
                .OrderByDescending(r => r.Date)
                .Select(r => (IReadOnlyList<string>)
                [
                    Date(r.Date), localizer.EnumText<PaymentKind>(r.Kind) ?? Empty, Amount(r.Amount, r.Currency), StatusText(r), Text(r.Description),
                ]),
            [2])));
    }

    /// <summary>Status is stored as the raw C# enum member name (<c>ToString()</c> of the source enum, which depends on
    /// <see cref="PaymentOverviewRow.Kind"/>); Overtime/Adjustment kinds have no status enum of their own yet and print raw.</summary>
    private string StatusText(PaymentOverviewRow row) => row.Kind switch
    {
        PaymentKind.Expense => localizer.EnumText(nameof(ExpenseClaimStatus), row.Status) ?? row.Status,
        PaymentKind.Advance => localizer.EnumText(nameof(AdvanceRequestStatus), row.Status) ?? row.Status,
        _ => row.Status,
    };
}

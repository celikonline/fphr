using Microsoft.Extensions.Localization;
using NSubstitute;
using Sense.Modules.Employee.Contracts.CardPdf;
using Sense.Modules.Expense.Application.Payments;
using Sense.Modules.Expense.Infrastructure.CardPdf;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Infrastructure;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Expense.Tests;

public sealed class ExpenseCardPdfSectionTests
{
    [Fact]
    public async Task Expense_FiltersPaymentsByPeriod()
    {
        var localizer = Substitute.For<IStringLocalizer<SharedResource>>();
        localizer[Arg.Any<string>()].Returns(call => new LocalizedString(call.Arg<string>(), call.Arg<string>(), resourceNotFound: true));
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.Query(Arg.Any<GetEmployeePaymentsOverviewQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<IReadOnlyList<PaymentOverviewRow>>(
            [
                new(PaymentKind.Expense, new DateOnly(2026, 4, 1), 100m, "TRY", "Approved", Guid.NewGuid(), "Taksi"),
                new(PaymentKind.Advance, new DateOnly(2024, 4, 1), 900m, "TRY", "Paid", Guid.NewGuid(), null),
            ])));
        var request = new EmployeeCardPdfRequest(Guid.NewGuid(), Guid.NewGuid(), false, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));

        var result = await new ExpenseCardPdfSection(dispatcher, localizer).BuildAsync(request, CancellationToken.None);

        result.Blocks!.OfType<SimplePdfTable>().Single().Rows.ShouldHaveSingleItem()[4].ShouldBe("Taksi");
    }
}

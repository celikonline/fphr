using Microsoft.Extensions.Localization;
using NSubstitute;
using Sense.Modules.Employee.Contracts.CardPdf;
using Sense.Modules.Payroll.Application.Garnishments;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Application.Profiles;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Infrastructure.CardPdf;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Infrastructure;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

public sealed class PayrollCardPdfSectionTests
{
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly EmployeeCardPdfRequest Request =
        new(Guid.NewGuid(), EmployeeId, false, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));

    private static IStringLocalizer<SharedResource> Localizer()
    {
        var localizer = Substitute.For<IStringLocalizer<SharedResource>>();
        localizer[Arg.Any<string>()].Returns(call => new LocalizedString(call.Arg<string>(), call.Arg<string>(), resourceNotFound: true));
        return localizer;
    }

    private static EmployeePayslipDto Payslip(int year, int month) =>
        new(Guid.NewGuid(), Guid.NewGuid(), year, month, PayrollPeriodStatus.Paid, 50000m, 38000m, 60000m);

    [Fact]
    public async Task Payroll_FiltersPayslipsByPeriod_AndShowsMissingProfileLine()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.Query(Arg.Any<GetCurrentEmployeePayrollProfileQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure<EmployeePayrollProfileDto>(Error.NotFound("payroll.profile_not_found"))));
        dispatcher.Query(Arg.Any<ListEmployeePayslipsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<IReadOnlyList<EmployeePayslipDto>>([Payslip(2026, 3), Payslip(2025, 3)])));
        dispatcher.Query(Arg.Any<ListGarnishmentOrdersQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<IReadOnlyList<GarnishmentOrderDto>>([])));

        var result = await new PayrollCardPdfSection(dispatcher, Localizer()).BuildAsync(Request, CancellationToken.None);

        result.Blocks!.OfType<SimplePdfTable>().Single(t => t.Heading == "Bordro pusulaları").Rows.ShouldHaveSingleItem()[0].ShouldBe("2026-03");
        result.Blocks!.OfType<SimplePdfParagraph>().ShouldContain(p => p.Text == "Bordro profili: kayıt yok.");
    }

    [Fact]
    public async Task Payroll_OwnCard_UsesMyPayslipsOnly()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.Query(Arg.Any<GetMyPayslipsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<IReadOnlyList<MyPayslipDto>>([])));

        var result = await new PayrollCardPdfSection(dispatcher, Localizer()).BuildAsync(Request with { IsOwnCard = true }, CancellationToken.None);

        result.IsAvailable.ShouldBeTrue();
        await dispatcher.DidNotReceive().Query(Arg.Any<GetCurrentEmployeePayrollProfileQuery>(), Arg.Any<CancellationToken>());
    }
}

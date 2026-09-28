using Microsoft.Extensions.Localization;
using NSubstitute;
using Sense.Modules.Employee.Application.Career;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Application.Payments;
using Sense.Modules.Employee.Contracts.CardPdf;
using Sense.Modules.Employee.Infrastructure.CardPdf;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Infrastructure;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Employee.Tests;

public sealed class EmployeeCardPdfSectionsTests
{
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly EmployeeCardPdfRequest AdminRequest = new(Guid.NewGuid(), EmployeeId, false, null, null);
    private static readonly EmployeeCardPdfRequest OwnRequest = AdminRequest with { IsOwnCard = true };

    private static IStringLocalizer<SharedResource> Localizer()
    {
        var localizer = Substitute.For<IStringLocalizer<SharedResource>>();
        localizer[Arg.Any<string>()].Returns(call => new LocalizedString(call.Arg<string>(), call.Arg<string>(), resourceNotFound: true));
        return localizer;
    }

    [Fact]
    public async Task General_WhenEmployeeQueryForbidden_IsUnavailable()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.Query(Arg.Any<GetEmployeeQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure<EmployeeDetailDto>(Error.Forbidden("auth.forbidden"))));

        var result = await new GeneralCardPdfSection(dispatcher, Localizer()).BuildAsync(AdminRequest, CancellationToken.None);

        result.IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public async Task Career_OwnCard_UsesMyQueryAndSkipsEmploymentHistory()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.Query(Arg.Any<GetMyCareerQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<IReadOnlyList<CareerRecordDto>>([])));

        var result = await new CareerCardPdfSection(dispatcher, Localizer()).BuildAsync(OwnRequest, CancellationToken.None);

        result.IsAvailable.ShouldBeTrue();
        await dispatcher.DidNotReceive().Query(Arg.Any<GetEmployeeCareerQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Payments_SalaryForbiddenBankAllowed_ShowsBankOnly()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.Query(Arg.Any<GetEmployeeSalariesQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure<IReadOnlyList<SalaryRecordDto>>(Error.Forbidden("auth.forbidden"))));
        dispatcher.Query(Arg.Any<GetEmployeeBankAccountsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<IReadOnlyList<BankAccountDto>>([])));

        var result = await new EmployeePaymentsCardPdfSection(dispatcher, Localizer()).BuildAsync(AdminRequest, CancellationToken.None);

        result.Blocks!.OfType<SimplePdfParagraph>().ShouldHaveSingleItem().Text.ShouldBe("Banka hesapları: kayıt yok.");
    }
}

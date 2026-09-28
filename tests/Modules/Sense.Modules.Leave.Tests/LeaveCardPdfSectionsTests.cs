using Microsoft.Extensions.Localization;
using NSubstitute;
using Sense.Modules.Employee.Contracts.CardPdf;
using Sense.Modules.Leave.Application.Balances;
using Sense.Modules.Leave.Application.Overtime;
using Sense.Modules.Leave.Application.Requests;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Leave.Infrastructure.CardPdf;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Infrastructure;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Leave.Tests;

public sealed class LeaveCardPdfSectionsTests
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

    private static LeaveRequestDto LeaveRequest(DateOnly start, DateOnly end) => new(
        Guid.NewGuid(), EmployeeId, Guid.NewGuid(), LeaveTypeCode.Annual, start, end, 1m, null, LeaveRequestStatus.Approved,
        LeaveRequestSource.SelfService, null, 0, DateTime.UtcNow, null, "Yıllık izin");

    private static IDispatcher Dispatcher(IReadOnlyList<LeaveRequestDto> requests, bool balancesForbidden = false)
    {
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.Query(Arg.Any<GetEmployeeLeaveBalancesQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(balancesForbidden
                ? Result.Failure<EmployeeLeaveBalancesDto>(Error.Forbidden("auth.forbidden"))
                : Result.Success(new EmployeeLeaveBalancesDto(EmployeeId, Guid.NewGuid(), new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 1), []))));
        dispatcher.Query(Arg.Any<ListEmployeeLeaveRequestsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(requests)));
        return dispatcher;
    }

    [Fact]
    public async Task Leave_FiltersRequestsByPeriod()
    {
        var inside = LeaveRequest(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 3));
        var outside = LeaveRequest(new DateOnly(2025, 3, 2), new DateOnly(2025, 3, 3));

        var result = await new LeaveCardPdfSection(Dispatcher([inside, outside]), Localizer()).BuildAsync(Request, CancellationToken.None);

        var table = result.Blocks!.OfType<SimplePdfTable>().Single(t => t.Heading == "İzin talepleri");
        table.Rows.ShouldHaveSingleItem()[1].ShouldBe("02.03.2026");
    }

    [Fact]
    public async Task Leave_BalancesForbidden_StillShowsRequests()
    {
        var dispatcher = Dispatcher([LeaveRequest(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 2))], balancesForbidden: true);

        var result = await new LeaveCardPdfSection(dispatcher, Localizer()).BuildAsync(Request, CancellationToken.None);

        result.Blocks!.OfType<SimplePdfTable>().ShouldHaveSingleItem().Heading.ShouldBe("İzin talepleri");
    }

    [Fact]
    public async Task Leave_OwnCard_UsesMyQueries()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.Query(Arg.Any<GetMyLeaveBalancesQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure<EmployeeLeaveBalancesDto>(Error.Forbidden("auth.forbidden"))));
        dispatcher.Query(Arg.Any<ListMyLeaveRequestsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<IReadOnlyList<LeaveRequestDto>>([])));

        var result = await new LeaveCardPdfSection(dispatcher, Localizer()).BuildAsync(Request with { IsOwnCard = true }, CancellationToken.None);

        result.IsAvailable.ShouldBeTrue();
        await dispatcher.DidNotReceive().Query(Arg.Any<ListEmployeeLeaveRequestsQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Overtime_AllForbidden_IsUnavailable()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.Query(Arg.Any<ListEmployeeOvertimeRequestsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure<IReadOnlyList<OvertimeRequestDto>>(Error.Forbidden("auth.forbidden"))));
        dispatcher.Query(Arg.Any<GetEmployeeAnnualOvertimeUsageQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure<AnnualOvertimeUsageDto>(Error.Forbidden("auth.forbidden"))));

        var result = await new OvertimeCardPdfSection(dispatcher, Localizer()).BuildAsync(Request, CancellationToken.None);

        result.IsAvailable.ShouldBeFalse();
    }
}

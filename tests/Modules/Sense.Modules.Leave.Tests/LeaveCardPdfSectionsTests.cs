using Microsoft.Extensions.Localization;
using NSubstitute;
using Sense.Modules.Employee.Contracts.CardPdf;
using Sense.Modules.Leave.Application.Balances;
using Sense.Modules.Leave.Application.Overtime;
using Sense.Modules.Leave.Application.Requests;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Balances;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Leave.Domain.Overtime;
using Sense.Modules.Leave.Infrastructure.CardPdf;
using Sense.Shared.Contracts.Configuration;
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
    public async Task Leave_ResolvesResourceKeyTypeNames_InBalancesAndRequestsTables()
    {
        var balance = new LeaveBalanceDto(
            "leave.type.annual", LeaveTypeCode.Annual,
            new LeaveBalance(
                Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), new DateOnly(2027, 1, 1),
                14m, 2m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 12m, 12m, null));
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.Query(Arg.Any<GetEmployeeLeaveBalancesQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(new EmployeeLeaveBalancesDto(EmployeeId, Guid.NewGuid(), new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 1), [balance]))));
        var request = LeaveRequest(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 3)) with { LeaveTypeName = "leave.type.annual" };
        dispatcher.Query(Arg.Any<ListEmployeeLeaveRequestsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<IReadOnlyList<LeaveRequestDto>>([request])));

        var localizer = Substitute.For<IStringLocalizer<SharedResource>>();
        localizer[Arg.Any<string>()].Returns(call => new LocalizedString(call.Arg<string>(), call.Arg<string>(), resourceNotFound: true));
        localizer["leave.type.annual"].Returns(new LocalizedString("leave.type.annual", "Yıllık izin", resourceNotFound: false));

        var result = await new LeaveCardPdfSection(dispatcher, localizer).BuildAsync(Request, CancellationToken.None);

        var tables = result.Blocks!.OfType<SimplePdfTable>().ToList();
        tables.Single(t => t.Heading == "İzin bakiyeleri").Rows.ShouldHaveSingleItem()[0].ShouldBe("Yıllık izin");
        tables.Single(t => t.Heading == "İzin talepleri").Rows.ShouldHaveSingleItem()[0].ShouldBe("Yıllık izin");
    }

    [Fact]
    public async Task Leave_UnknownCustomTypeName_PassesThroughUnchanged_InBalancesAndRequestsTables()
    {
        var balance = new LeaveBalanceDto(
            "Özel İzin Türü", LeaveTypeCode.Annual,
            new LeaveBalance(
                Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), new DateOnly(2027, 1, 1),
                14m, 2m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 12m, 12m, null));
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.Query(Arg.Any<GetEmployeeLeaveBalancesQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(new EmployeeLeaveBalancesDto(EmployeeId, Guid.NewGuid(), new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 1), [balance]))));
        var request = LeaveRequest(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 3)) with { LeaveTypeName = "Özel İzin Türü" };
        dispatcher.Query(Arg.Any<ListEmployeeLeaveRequestsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<IReadOnlyList<LeaveRequestDto>>([request])));

        var result = await new LeaveCardPdfSection(dispatcher, Localizer()).BuildAsync(Request, CancellationToken.None);

        var tables = result.Blocks!.OfType<SimplePdfTable>().ToList();
        tables.Single(t => t.Heading == "İzin bakiyeleri").Rows.ShouldHaveSingleItem()[0].ShouldBe("Özel İzin Türü");
        tables.Single(t => t.Heading == "İzin talepleri").Rows.ShouldHaveSingleItem()[0].ShouldBe("Özel İzin Türü");
    }

    [Fact]
    public async Task Leave_RequestsAtMaxPageSize_AppendsTruncationNote()
    {
        var requests = Enumerable.Range(0, PagingDefaults.MaxPageSize)
            .Select(_ => LeaveRequest(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 1)))
            .ToList();

        var result = await new LeaveCardPdfSection(Dispatcher(requests), Localizer()).BuildAsync(Request, CancellationToken.None);

        result.Blocks!.OfType<SimplePdfParagraph>()
            .ShouldContain(p => p.Text == $"Yalnızca en yeni {PagingDefaults.MaxPageSize} kayıt gösterildi.");
    }

    [Fact]
    public async Task Leave_RequestsUnderMaxPageSize_NoTruncationNote()
    {
        var requests = new[] { LeaveRequest(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 1)) };

        var result = await new LeaveCardPdfSection(Dispatcher(requests), Localizer()).BuildAsync(Request, CancellationToken.None);

        result.Blocks!.OfType<SimplePdfParagraph>().ShouldNotContain(p => p.Text.Contains("Yalnızca en yeni"));
    }

    private static OvertimeRequestDto OvertimeRequest(DateOnly workDate) => new(
        Guid.NewGuid(), EmployeeId, workDate, new TimeOnly(9, 0), 60, DateTime.UtcNow, DateTime.UtcNow, OvertimeDayKind.Workday,
        0, 60, null, OvertimeRequestSource.Manual, OvertimeRequestStatus.Approved, null, DateTime.UtcNow, null, null,
        OvertimePayRateKind.Rate25, 1m);

    [Fact]
    public async Task Overtime_RequestsAtMaxPageSize_AppendsTruncationNote()
    {
        var list = Enumerable.Range(0, PagingDefaults.MaxPageSize)
            .Select(_ => OvertimeRequest(new DateOnly(2026, 3, 1)))
            .ToList();
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.Query(Arg.Any<GetEmployeeAnnualOvertimeUsageQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure<AnnualOvertimeUsageDto>(Error.Forbidden("auth.forbidden"))));
        dispatcher.Query(Arg.Any<ListEmployeeOvertimeRequestsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<IReadOnlyList<OvertimeRequestDto>>(list)));

        var result = await new OvertimeCardPdfSection(dispatcher, Localizer()).BuildAsync(Request, CancellationToken.None);

        result.Blocks!.OfType<SimplePdfParagraph>()
            .ShouldContain(p => p.Text == $"Yalnızca en yeni {PagingDefaults.MaxPageSize} kayıt gösterildi.");
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

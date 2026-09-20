using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Leave.Application;
using Sense.Modules.Leave.Application.Requests;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Leave.Domain.RuleSets;
using Shouldly;
using Xunit;

namespace Sense.Modules.Leave.Tests;

/// <summary>Denetim raporu A5: "Vekil ataması zorunlu" tür ayarı talep formunda ve sunucuda uygulanır.</summary>
public sealed class LeaveSubstituteTests
{
    private static readonly DateOnly Start = new(2026, 10, 5);

    // Sabit saat: geriye dönük talep sınırı (LeaveBackdatePolicy) testin sabit tarihlerini gerçek takvime bağlamasın.
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public void Submit_StoresTheSubstitute_AndUpdatePendingReplacesIt()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var request = LeaveRequest.Submit(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LeaveTypeCode.Annual, Start, Start.AddDays(2), 3m, null,
            LeaveRequestSource.SelfService, DateTime.UtcNow, null, null, substituteEmployeeId: first).Value;
        request.SubstituteEmployeeId.ShouldBe(first);

        request.UpdatePending(request.LeaveTypeId, LeaveTypeCode.Annual, Start, Start.AddDays(1), 2m, null, substituteEmployeeId: second).IsSuccess.ShouldBeTrue();
        request.SubstituteEmployeeId.ShouldBe(second);

        request.UpdatePending(request.LeaveTypeId, LeaveTypeCode.Annual, Start, Start.AddDays(1), 2m, null).IsSuccess.ShouldBeTrue();
        request.SubstituteEmployeeId.ShouldBeNull();
    }

    private sealed record Fixture(LeaveRequestRules Rules, LeaveType Type, Guid EmployeeId, Guid SubstituteId, ILeaveRequestRepository Requests, IEmployeeLookup Employees);

    private static Fixture NewFixture(bool requiresSubstitute = true, bool substituteExists = true)
    {
        var tenantId = Guid.NewGuid();
        var set = LeaveRuleSetSeed.CreateTurkishLabourLawRuleSet(tenantId, StatutoryLeaveDurations.Turkey2026);
        var marriage = set.Types.Single(t => t.Code == LeaveTypeCode.Marriage);
        set.UpdateType(marriage.Id, marriage.ToSettings() with { RequiresSubstitute = requiresSubstitute }).IsSuccess.ShouldBeTrue();
        var substituteId = Guid.NewGuid();
        var requests = Substitute.For<ILeaveRequestRepository>();
        requests.ListActiveOverlappingAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([]);
        var employees = Substitute.For<IEmployeeLookup>();
        employees.ExistsActiveAsync(Arg.Any<Guid>(), substituteId, Arg.Any<CancellationToken>()).Returns(substituteExists);
        var rules = new LeaveRequestRules(
            Substitute.For<IEmployeeLeaveProfileRepository>(), Substitute.For<ILeaveRuleSetRepository>(), requests, Substitute.For<ILeaveLedgerRepository>(),
            Substitute.For<ILeaveDayCalculator>(), null!, new LeaveBackdatePolicy(new FixedTimeProvider(new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero))), employees);
        return new Fixture(rules, set.Types.Single(t => t.Code == LeaveTypeCode.Marriage), Guid.NewGuid(), substituteId, requests, employees);
    }

    private static Task<Sense.Shared.Kernel.Results.Result> ValidateAsync(
        Fixture f, Guid? substitute, TimeOnly? startTime = null, TimeOnly? endTime = null, HalfDayPeriod? halfDay = null) =>
        f.Rules.ValidateSubstituteAsync(Guid.NewGuid(), f.EmployeeId, f.Type, substitute, Start, Start.AddDays(2), startTime, endTime, halfDay, CancellationToken.None);

    [Fact]
    public async Task Missing_WhenTypeRequiresSubstitute_IsRejected()
    {
        var fixture = NewFixture();

        (await ValidateAsync(fixture, null)).Error.Code.ShouldBe(LeaveRequestErrors.SubstituteRequired);
    }

    [Fact]
    public async Task Missing_WhenTypeDoesNotRequireSubstitute_IsAccepted()
    {
        (await ValidateAsync(NewFixture(requiresSubstitute: false), null)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Requester_CannotBeTheirOwnSubstitute()
    {
        var fixture = NewFixture();

        (await ValidateAsync(fixture, fixture.EmployeeId)).Error.Code.ShouldBe(LeaveRequestErrors.SubstituteIsRequester);
    }

    [Fact]
    public async Task UnknownOrInactiveSubstitute_IsRejected()
    {
        var fixture = NewFixture(substituteExists: false);

        (await ValidateAsync(fixture, fixture.SubstituteId)).Error.Code.ShouldBe(LeaveRequestErrors.SubstituteNotFound);
    }

    [Fact]
    public async Task Substitute_WithActiveLeaveOverlappingTheDates_IsRejected()
    {
        var fixture = NewFixture();
        var substituteLeave = LeaveRequest.Submit(
            Guid.NewGuid(), fixture.SubstituteId, Guid.NewGuid(), LeaveTypeCode.Annual, Start.AddDays(2), Start.AddDays(4), 3m, null,
            LeaveRequestSource.SelfService, DateTime.UtcNow, null, null).Value;
        fixture.Requests.ListActiveOverlappingAsync(fixture.SubstituteId, Start, Start.AddDays(2), Arg.Any<CancellationToken>()).Returns([substituteLeave]);

        (await ValidateAsync(fixture, fixture.SubstituteId)).Error.Code.ShouldBe(LeaveRequestErrors.SubstituteOnLeave);
    }

    [Fact]
    public async Task Substitute_WithHalfDayLeaveThatDoesNotClashWithTheRequestedHours_IsAccepted()
    {
        var fixture = NewFixture();
        var day = Start;
        var morningLeave = LeaveRequest.Submit(
            Guid.NewGuid(), fixture.SubstituteId, Guid.NewGuid(), LeaveTypeCode.Excuse, day, day, 0.5m, null,
            LeaveRequestSource.SelfService, DateTime.UtcNow, null, null, halfDayPeriod: HalfDayPeriod.Morning).Value;
        fixture.Requests.ListActiveOverlappingAsync(fixture.SubstituteId, day, day, Arg.Any<CancellationToken>()).Returns([morningLeave]);

        var result = await fixture.Rules.ValidateSubstituteAsync(
            Guid.NewGuid(), fixture.EmployeeId, fixture.Type, fixture.SubstituteId, day, day, new TimeOnly(14, 0), new TimeOnly(16, 0), null, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task AvailableActiveSubstitute_IsAccepted()
    {
        var fixture = NewFixture();

        (await ValidateAsync(fixture, fixture.SubstituteId)).IsSuccess.ShouldBeTrue();
    }
}

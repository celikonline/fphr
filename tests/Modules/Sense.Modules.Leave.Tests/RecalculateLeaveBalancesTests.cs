using NSubstitute;
using Sense.Modules.Leave.Application;
using Sense.Modules.Leave.Application.Balances;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Ledger;
using Sense.Modules.Leave.Domain.Profiles;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Shared.Contracts.Legislation;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Leave.Tests;

/// <summary>
/// TK-013: <see cref="RecalculateLeaveBalancesCommand.RecalculateFrom"/> is the optional start date that was missing
/// (docs/analysis/kolayik-video/faz1b-durum-takibi.md #17 - "RecalculateFrom tarih parametresi yok, 0 sonuç dönüyor").
/// These tests cover the handler/service wiring; <see cref="LeaveBalanceEngineTests"/> (LeaveDomainTests.cs) covers
/// the pure regeneration math.
/// </summary>
public sealed class RecalculateLeaveBalancesTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly DateOnly HireDate = new(2020, 1, 1);
    private static readonly DateOnly Today = new(2023, 6, 1); // > 1 accrual per year since HireDate

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>A rule set with a single PerYear (Annual) type, so exactly one AddRange/RemoveRange call happens per recalculation.</summary>
    private static LeaveRuleSet BuildSingleTypeRuleSet()
    {
        var set = LeaveRuleSet.Create(TenantId, "Set").Value;
        var annual = LeaveRuleSetSeed.Types(StatutoryLeaveDurations.Turkey2026).First(t => t.Code == LeaveTypeCode.Annual);
        set.AddType(annual);
        return set;
    }

    private static (RecalculateLeaveBalancesHandler Handler, EmployeeLeaveProfile Profile, ILeaveLedgerRepository Ledger) Build(
        IReadOnlyList<LeaveLedgerEntry>? existingLedgerRows = null, LeaveRuleSet? ruleSet = null)
    {
        ruleSet ??= BuildSingleTypeRuleSet();
        var profile = EmployeeLeaveProfile.Create(TenantId, EmployeeId, ruleSet.Id, HireDate, HireDate);

        var profiles = Substitute.For<IEmployeeLeaveProfileRepository>();
        profiles.FindByEmployeeIdAsync(EmployeeId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeLeaveProfile?>(profile));

        var ruleSets = Substitute.For<ILeaveRuleSetRepository>();
        ruleSets.GetByIdAsync(ruleSet.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(ruleSet)));

        var dataScope = Substitute.For<IDataScopeService>();
        dataScope.CanAccessEmployeeAsync(Arg.Any<string>(), EmployeeId, Arg.Any<CancellationToken>()).Returns(true);

        var ledger = Substitute.For<ILeaveLedgerRepository>();
        ledger.ListAsync(EmployeeId, null, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<LeaveLedgerEntry>>(existingLedgerRows ?? []));

        var legislation = Substitute.For<ILegislationParameterReader>();
        legislation.ListAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<LegislationParameterValue>>([]));

        var balances = new LeaveBalanceService(ruleSets, ledger, legislation, new FixedTimeProvider(Today.ToDateTime(default, DateTimeKind.Utc)));
        var handler = new RecalculateLeaveBalancesHandler(profiles, ruleSets, dataScope, balances);
        return (handler, profile, ledger);
    }

    [Fact]
    public async Task Handle_RecalculateFromOmitted_RegeneratesEverythingFromAccrualStart_UnchangedBehavior()
    {
        var (handler, _, ledger) = Build();

        var result = await handler.Handle(new RecalculateLeaveBalancesCommand(EmployeeId), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        ledger.Received(1).AddRange(Arg.Is<IEnumerable<LeaveLedgerEntry>>(entries =>
            entries.Any(e => e.Type == LeaveLedgerEntryType.Accrual)));
        // No pre-existing rows to remove, but the removal set must not be filtered to "on/after a cutoff" - it is the
        // full generated set, exactly like before this feature existed.
        ledger.Received(1).RemoveRange(Arg.Is<IEnumerable<LeaveLedgerEntry>>(entries => !entries.Any()));
    }

    [Fact]
    public async Task Handle_ValidRecalculateFrom_OnlyRebuildsRowsFromThatDateForward_AndReturnsNonEmptyResults()
    {
        // A prior full recalculation already produced generated rows for every year since HireDate. Reuse the same rule
        // set (and its type Id) for the bootstrap and the real run below, otherwise the existing rows' LeaveTypeId would
        // not match the second run's freshly-generated type Id and the handler would see nothing to rebuild.
        var ruleSet = BuildSingleTypeRuleSet();
        var (bootstrapHandler, _, bootstrapLedger) = Build(ruleSet: ruleSet);
        await bootstrapHandler.Handle(new RecalculateLeaveBalancesCommand(EmployeeId), CancellationToken.None);
        var priorRows = bootstrapLedger.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(ILeaveLedgerRepository.AddRange))
            .GetArguments()[0] as IEnumerable<LeaveLedgerEntry>;
        priorRows.ShouldNotBeNull();
        var existing = priorRows!.ToList();
        existing.ShouldNotBeEmpty();

        var cutoff = new DateOnly(2022, 1, 1); // a period boundary well after HireDate, before Today
        var (handler, _, ledger) = Build(existing, ruleSet);

        var result = await handler.Handle(new RecalculateLeaveBalancesCommand(EmployeeId, cutoff), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        ledger.Received(1).RemoveRange(Arg.Is<IEnumerable<LeaveLedgerEntry>>(entries =>
            entries.All(e => e.EffectiveDate >= cutoff) && entries.Any()));
        ledger.Received(1).AddRange(Arg.Is<IEnumerable<LeaveLedgerEntry>>(entries =>
            entries.Any() && entries.All(e => e.EffectiveDate >= cutoff)));
    }

    [Fact]
    public async Task Handle_RecalculateFromInTheFuture_IsRejectedAsValidation_AndLeavesTheLedgerUntouched()
    {
        var (handler, _, ledger) = Build();
        var future = Today.AddDays(1);

        var result = await handler.Handle(new RecalculateLeaveBalancesCommand(EmployeeId, future), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe(LeaveErrors.RecalculateFromInFuture);
        ledger.DidNotReceive().AddRange(Arg.Any<IEnumerable<LeaveLedgerEntry>>());
        ledger.DidNotReceive().RemoveRange(Arg.Any<IEnumerable<LeaveLedgerEntry>>());
    }

    [Fact]
    public async Task Handle_RecalculateFromIsDefaultDateOnly_ClampsToAccrualStart_AndStillReturnsNonEmptyResults()
    {
        // "Geçersiz" boundary at this layer: default(DateOnly) (0001-01-01), long before HireDate. The engine clamps
        // it to the profile's accrual start instead of failing or silently producing zero rows.
        var (handler, _, ledger) = Build();

        var result = await handler.Handle(new RecalculateLeaveBalancesCommand(EmployeeId, default(DateOnly)), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        ledger.Received(1).AddRange(Arg.Is<IEnumerable<LeaveLedgerEntry>>(entries =>
            entries.Any(e => e.Type == LeaveLedgerEntryType.Accrual)));
    }
}

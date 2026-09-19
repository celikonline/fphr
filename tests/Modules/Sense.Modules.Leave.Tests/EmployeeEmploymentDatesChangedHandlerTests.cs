using NSubstitute;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Leave.Application;
using Sense.Modules.Leave.Application.Balances;
using Sense.Modules.Leave.Application.Employees;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Ledger;
using Sense.Modules.Leave.Domain.Profiles;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Shared.Contracts.Legislation;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Leave.Tests;

/// <summary>
/// senseik-044 (C-02): a hire-date correction on an already-hired employee must move the leave profile's dates and
/// regenerate the accruals from the corrected start - previously the profile kept accruing from the original date.
/// </summary>
public sealed class EmployeeEmploymentDatesChangedHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly DateOnly OriginalHireDate = new(2024, 1, 1);
    private static readonly DateOnly Today = new(2025, 1, 1); // one completed year after OriginalHireDate

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static (EmployeeEmploymentDatesChangedHandler Handler, EmployeeLeaveProfile Profile, ILeaveLedgerRepository Ledger, ILeaveUnitOfWork UnitOfWork)
        Build(bool profileExists = true)
    {
        var ruleSet = LeaveRuleSetSeed.CreateTurkishLabourLawRuleSet(TenantId, StatutoryLeaveDurations.Turkey2026);
        var profile = EmployeeLeaveProfile.Create(TenantId, EmployeeId, ruleSet.Id, OriginalHireDate, OriginalHireDate);

        var profiles = Substitute.For<IEmployeeLeaveProfileRepository>();
        profiles.FindByEmployeeIdAsync(EmployeeId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(profileExists ? profile : null));

        var ruleSets = Substitute.For<ILeaveRuleSetRepository>();
        ruleSets.GetByIdAsync(ruleSet.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(ruleSet)));

        var ledger = Substitute.For<ILeaveLedgerRepository>();
        ledger.ListAsync(EmployeeId, null, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<LeaveLedgerEntry>>([]));

        var legislation = Substitute.For<ILegislationParameterReader>();
        legislation.ListAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<LegislationParameterValue>>([]));

        var balances = new LeaveBalanceService(ruleSets, ledger, legislation, new FixedTimeProvider(Today.ToDateTime(default, DateTimeKind.Utc)));
        var unitOfWork = Substitute.For<ILeaveUnitOfWork>();
        return (new EmployeeEmploymentDatesChangedHandler(profiles, ruleSets, balances, unitOfWork), profile, ledger, unitOfWork);
    }

    [Fact]
    public async Task Handle_HireDateMovedLater_UpdatesProfileDates_AndRegeneratesWithoutTheNoLongerEarnedAccrual()
    {
        var (handler, profile, ledger, unitOfWork) = Build();
        var corrected = new DateOnly(2024, 6, 1); // less than a year before Today -> no annual accrual yet

        await handler.Handle(new EmployeeEmploymentDatesChanged(TenantId, EmployeeId, corrected, corrected), CancellationToken.None);

        profile.HireDate.ShouldBe(corrected);
        profile.LeaveAccrualStartDate.ShouldBe(corrected);
        ledger.Received(1).AddRange(Arg.Is<IEnumerable<LeaveLedgerEntry>>(entries =>
            !entries.Any(e => e.Type == LeaveLedgerEntryType.Accrual && e.Amount > 0)));
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SameDates_IsANoOp()
    {
        var (handler, _, ledger, unitOfWork) = Build();

        await handler.Handle(new EmployeeEmploymentDatesChanged(TenantId, EmployeeId, OriginalHireDate, OriginalHireDate), CancellationToken.None);

        ledger.DidNotReceive().AddRange(Arg.Any<IEnumerable<LeaveLedgerEntry>>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoProfileYet_DoesNothing()
    {
        var (handler, _, ledger, unitOfWork) = Build(profileExists: false);

        await handler.Handle(new EmployeeEmploymentDatesChanged(TenantId, EmployeeId, Today, Today), CancellationToken.None);

        ledger.DidNotReceive().AddRange(Arg.Any<IEnumerable<LeaveLedgerEntry>>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}

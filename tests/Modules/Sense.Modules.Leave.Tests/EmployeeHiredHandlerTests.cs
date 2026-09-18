using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Leave.Application;
using Sense.Modules.Leave.Application.Balances;
using Sense.Modules.Leave.Application.Employees;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Ledger;
using Sense.Modules.Leave.Domain.Profiles;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Shared.Contracts.Legislation;
using Xunit;

namespace Sense.Modules.Leave.Tests;

/// <summary>
/// senseik-007 (A-01 regresyon): EmployeeHiredHandler is the wiring point that was missing - it must read BirthDate
/// through IEmployeeBirthDateLookup (never carried on EmployeeHired itself, KVKK) and set it on the new
/// EmployeeLeaveProfile before the first RecalculateAsync, so the ≤18/≥50 statutory age exception (4857 md.53/IV)
/// can already apply to the very first accrual.
/// </summary>
public sealed class EmployeeHiredHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly DateOnly HireDate = new(2024, 1, 1);
    private static readonly DateOnly Today = new(2025, 1, 1); // exactly one completed year after HireDate

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static ILegislationParameterReader EmptyLegislation()
    {
        var reader = Substitute.For<ILegislationParameterReader>();
        reader.ListAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<LegislationParameterValue>>([]));
        return reader;
    }

    private static (EmployeeHiredHandler Handler, IEmployeeLeaveProfileRepository Profiles, ILeaveLedgerRepository Ledger, IEmployeeBirthDateLookup BirthDates)
        BuildHandler(DateOnly? birthDate)
    {
        var ruleSet = LeaveRuleSetSeed.CreateTurkishLabourLawRuleSet(TenantId, StatutoryLeaveDurations.Turkey2026);

        var profiles = Substitute.For<IEmployeeLeaveProfileRepository>();
        profiles.FindByEmployeeIdAsync(EmployeeId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeLeaveProfile?>(null));

        var ruleSets = Substitute.For<ILeaveRuleSetRepository>();
        ruleSets.FindDefaultAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<LeaveRuleSet?>(ruleSet));

        var ledger = Substitute.For<ILeaveLedgerRepository>();
        ledger.ListAsync(EmployeeId, null, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<LeaveLedgerEntry>>([]));

        var balances = new LeaveBalanceService(ruleSets, ledger, EmptyLegislation(), new FixedTimeProvider(Today.ToDateTime(default, DateTimeKind.Utc)));
        var unitOfWork = Substitute.For<ILeaveUnitOfWork>();

        var birthDates = Substitute.For<IEmployeeBirthDateLookup>();
        birthDates.GetBirthDateAsync(TenantId, EmployeeId, Arg.Any<CancellationToken>()).Returns(Task.FromResult(birthDate));

        var handler = new EmployeeHiredHandler(profiles, balances, unitOfWork, birthDates);
        return (handler, profiles, ledger, birthDates);
    }

    [Fact]
    public async Task Handle_EmployeeIsFiftyFiveYearsOld_SetsProfileBirthDate_AndFirstAccrualUsesTheTwentyDayAgeFloor()
    {
        var birthDate = Today.AddYears(-55); // Tier1 alone (years=1) would give 14; the age exception must lift it to 20.
        var (handler, profiles, ledger, _) = BuildHandler(birthDate);

        await handler.Handle(new EmployeeHired(TenantId, EmployeeId, HireDate, HireDate), CancellationToken.None);

        profiles.Received(1).Add(Arg.Is<EmployeeLeaveProfile>(p => p.BirthDate == birthDate && p.EmployeeId == EmployeeId));
        ledger.Received(1).AddRange(Arg.Is<IEnumerable<LeaveLedgerEntry>>(entries =>
            entries.Any(e => e.Type == LeaveLedgerEntryType.Accrual && e.Amount == 20m && e.EffectiveDate == Today)));
    }

    [Fact]
    public async Task Handle_NoBirthDateRecorded_LeavesProfileBirthDateNull_AndFirstAccrualUsesSeniorityTierOnly()
    {
        var (handler, profiles, ledger, _) = BuildHandler(birthDate: null);

        await handler.Handle(new EmployeeHired(TenantId, EmployeeId, HireDate, HireDate), CancellationToken.None);

        profiles.Received(1).Add(Arg.Is<EmployeeLeaveProfile>(p => p.BirthDate == null));
        ledger.Received(1).AddRange(Arg.Is<IEnumerable<LeaveLedgerEntry>>(entries =>
            entries.Any(e => e.Type == LeaveLedgerEntryType.Accrual && e.Amount == 14m && e.EffectiveDate == Today)));
    }

    [Fact]
    public async Task Handle_ProfileAlreadyExists_IsIdempotent_AndNeverCallsTheBirthDateLookup()
    {
        var existing = EmployeeLeaveProfile.Create(TenantId, EmployeeId, Guid.NewGuid(), HireDate, HireDate);
        var (handler, profiles, ledger, birthDates) = BuildHandler(birthDate: Today.AddYears(-60));
        profiles.FindByEmployeeIdAsync(EmployeeId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeLeaveProfile?>(existing));

        await handler.Handle(new EmployeeHired(TenantId, EmployeeId, HireDate, HireDate), CancellationToken.None);

        profiles.DidNotReceive().Add(Arg.Any<EmployeeLeaveProfile>());
        ledger.DidNotReceive().AddRange(Arg.Any<IEnumerable<LeaveLedgerEntry>>());
        await birthDates.DidNotReceive().GetBirthDateAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}

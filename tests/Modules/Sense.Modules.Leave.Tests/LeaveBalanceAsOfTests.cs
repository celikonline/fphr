using NSubstitute;
using Sense.Modules.Leave.Application.Balances;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Ledger;
using Sense.Modules.Leave.Domain.Profiles;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Shared.Contracts.Legislation;
using Shouldly;
using Xunit;

namespace Sense.Modules.Leave.Tests;

public sealed class LeaveBalanceAsOfTests
{
    [Theory]
    [InlineData(2024, 12, 1, 0)]
    [InlineData(2025, 7, 1, 9)]
    [InlineData(2026, 7, 1, 19)]
    public async Task Projection_RebuildsEntitlementAtTheRequestedDate_WithoutFutureRowsOrWrites(int year, int month, int day, int expected)
    {
        var tenant = Guid.NewGuid();
        var employee = Guid.NewGuid();
        var hire = new DateOnly(2024, 1, 1);
        var rules = LeaveRuleSet.Create(tenant, "Annual").Value;
        rules.AddType(LeaveRuleSetSeed.Types(StatutoryLeaveDurations.Turkey2026).First(t => t.Code == LeaveTypeCode.Annual));
        var type = rules.Types.Single();
        var profile = EmployeeLeaveProfile.Create(tenant, employee, rules.Id, hire, hire);
        var ledger = Substitute.For<ILeaveLedgerRepository>();
        ledger.ListAsync(employee, null, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<LeaveLedgerEntry>>(
            [LeaveLedgerEntry.Usage(tenant, employee, type.Id, 5m, new DateOnly(2025, 6, 1), LeaveLedgerSource.Manual, null),
             LeaveLedgerEntry.Usage(tenant, employee, type.Id, 4m, new DateOnly(2026, 1, 15), LeaveLedgerSource.Manual, null),
             // Persisted accruals from today's projection must not leak into a historical exit estimate.
             LeaveLedgerEntry.Accrual(tenant, employee, type.Id, 99m, new DateOnly(2026, 1, 1))]));
        var legislation = Substitute.For<ILegislationParameterReader>();
        legislation.ListAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<LegislationParameterValue>>([]));
        var service = new LeaveBalanceService(Substitute.For<ILeaveRuleSetRepository>(), ledger, legislation, TimeProvider.System);

        var result = await service.ProjectAsOfAsync(profile, rules, new DateOnly(year, month, day), CancellationToken.None);

        result.Single().CurrentBalance.ShouldBe(expected);
        ledger.DidNotReceive().AddRange(Arg.Any<IEnumerable<LeaveLedgerEntry>>());
        ledger.DidNotReceive().RemoveRange(Arg.Any<IEnumerable<LeaveLedgerEntry>>());
    }
}

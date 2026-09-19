using Sense.Modules.Payroll.Domain.Unions;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>Pure-function tests for <see cref="UnionDuesCalculator"/> (Faz 3 #58, 11.3.07-bordro.md "aidat kuralı").</summary>
public sealed class UnionDuesCalculatorTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid UnionId = Guid.NewGuid();
    private static readonly DateOnly PeriodMonth = new(2026, 6, 1);

    private static EmployeeUnionMembership Membership(
        UnionCoverageType coverageType = UnionCoverageType.Member,
        UnionDuesRule duesRule = UnionDuesRule.PercentOfGross,
        decimal duesValue = 0.01m,
        DateOnly? notifiedToEmployerAt = null,
        DateOnly? endDate = null) =>
        EmployeeUnionMembership.Create(
            TenantId, EmployeeId,
            new EmployeeUnionMembershipData(
                UnionId, new DateOnly(2026, 1, 1), notifiedToEmployerAt ?? new DateOnly(2026, 1, 1),
                coverageType, duesRule, duesValue, endDate)).Value;

    [Fact]
    public void Calculate_NoMembership_ReturnsZero() =>
        UnionDuesCalculator.Calculate(null, 100_000m, 30, PeriodMonth).ShouldBe(0m);

    [Fact]
    public void Calculate_PercentOfGross_ReturnsGrossTimesRate()
    {
        var membership = Membership(duesRule: UnionDuesRule.PercentOfGross, duesValue: 0.015m);

        UnionDuesCalculator.Calculate(membership, 100_000m, 30, PeriodMonth).ShouldBe(1_500m);
    }

    [Fact]
    public void Calculate_FixedAmount_ReturnsDuesValueRegardlessOfGross()
    {
        var membership = Membership(duesRule: UnionDuesRule.FixedAmount, duesValue: 250m);

        UnionDuesCalculator.Calculate(membership, 100_000m, 30, PeriodMonth).ShouldBe(250m);
    }

    [Fact]
    public void Calculate_DailyWage_ReturnsDailyWageTimesDuesValue()
    {
        // Daily wage = 100,000 / 30 = 3,333.33...; DuesValue = 1 (one day's wage).
        var membership = Membership(duesRule: UnionDuesRule.DailyWage, duesValue: 1m);

        UnionDuesCalculator.Calculate(membership, 100_000m, 30, PeriodMonth).ShouldBe(3_333.33m);
    }

    [Fact]
    public void Calculate_BeforeFirstEligiblePeriod_ReturnsZero()
    {
        var membership = Membership(notifiedToEmployerAt: new DateOnly(2026, 6, 1));

        // First eligible period is 2026-07 (the month after the notification's month).
        UnionDuesCalculator.Calculate(membership, 100_000m, 30, PeriodMonth).ShouldBe(0m);
    }

    [Theory]
    [InlineData(UnionCoverageType.UnionApproved)]
    [InlineData(UnionCoverageType.Excluded)]
    public void Calculate_NonDuesCoverage_ReturnsZero(UnionCoverageType coverage)
    {
        var membership = Membership(coverageType: coverage, duesValue: 0m);

        UnionDuesCalculator.Calculate(membership, 100_000m, 30, PeriodMonth).ShouldBe(0m);
    }

    [Fact]
    public void Calculate_SolidarityDues_BehavesLikeMemberDues()
    {
        var membership = Membership(coverageType: UnionCoverageType.SolidarityDues, duesRule: UnionDuesRule.PercentOfGross, duesValue: 0.01m);

        UnionDuesCalculator.Calculate(membership, 100_000m, 30, PeriodMonth).ShouldBe(1_000m);
    }

    [Fact]
    public void Calculate_AfterMembershipEnded_ReturnsZero()
    {
        var membership = Membership(endDate: new DateOnly(2026, 3, 15));

        UnionDuesCalculator.Calculate(membership, 100_000m, 30, PeriodMonth).ShouldBe(0m);
    }
}

using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Unions;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>Domain-only tests for <see cref="EmployeeUnionMembership"/> (Faz 3 #58, 11.3.07-bordro.md "Sendika / TİS").</summary>
public sealed class EmployeeUnionMembershipTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid UnionId = Guid.NewGuid();

    private static EmployeeUnionMembershipData Data(
        DateOnly? membershipDate = null, DateOnly? notifiedToEmployerAt = null,
        UnionCoverageType coverageType = UnionCoverageType.Member,
        UnionDuesRule duesRule = UnionDuesRule.PercentOfGross, decimal duesValue = 0.01m,
        DateOnly? endDate = null, Guid? unionId = null) =>
        new(
            unionId ?? UnionId,
            membershipDate ?? new DateOnly(2026, 1, 10),
            notifiedToEmployerAt ?? new DateOnly(2026, 1, 15),
            coverageType, duesRule, duesValue, endDate);

    [Fact]
    public void Create_WithValidData_Succeeds()
    {
        var result = EmployeeUnionMembership.Create(TenantId, EmployeeId, Data());

        result.IsSuccess.ShouldBeTrue();
        result.Value.EmployeeId.ShouldBe(EmployeeId);
        result.Value.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public void Create_WithoutUnion_Fails()
    {
        var result = EmployeeUnionMembership.Create(TenantId, EmployeeId, Data(unionId: Guid.Empty));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.UnionMembershipUnionRequired);
    }

    [Fact]
    public void Create_NotifiedBeforeMembershipDate_Fails()
    {
        var result = EmployeeUnionMembership.Create(
            TenantId, EmployeeId, Data(membershipDate: new DateOnly(2026, 2, 1), notifiedToEmployerAt: new DateOnly(2026, 1, 1)));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.UnionMembershipDateRangeInvalid);
    }

    [Theory]
    [InlineData(UnionCoverageType.Member)]
    [InlineData(UnionCoverageType.SolidarityDues)]
    public void Create_DuesApplicableCoverageWithZeroDues_Fails(UnionCoverageType coverage)
    {
        var result = EmployeeUnionMembership.Create(TenantId, EmployeeId, Data(coverageType: coverage, duesValue: 0m));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.UnionMembershipDuesValueInvalid);
    }

    [Fact]
    public void Create_PercentOfGrossAbove100Percent_Fails()
    {
        var result = EmployeeUnionMembership.Create(TenantId, EmployeeId, Data(duesRule: UnionDuesRule.PercentOfGross, duesValue: 1.5m));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.UnionMembershipDuesValueInvalid);
    }

    [Theory]
    [InlineData(UnionCoverageType.UnionApproved)]
    [InlineData(UnionCoverageType.Excluded)]
    public void Create_NonDuesCoverageWithZeroDues_Succeeds(UnionCoverageType coverage)
    {
        var result = EmployeeUnionMembership.Create(TenantId, EmployeeId, Data(coverageType: coverage, duesValue: 0m));

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void End_BeforeMembershipDate_Fails()
    {
        var membership = EmployeeUnionMembership.Create(
            TenantId, EmployeeId, Data(membershipDate: new DateOnly(2026, 3, 1), notifiedToEmployerAt: new DateOnly(2026, 3, 15))).Value;

        var result = membership.End(new DateOnly(2026, 1, 1));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.UnionMembershipDateRangeInvalid);
    }

    [Fact]
    public void End_OnOrAfterMembershipDate_ClosesMembership()
    {
        var membership = EmployeeUnionMembership.Create(TenantId, EmployeeId, Data()).Value;

        var result = membership.End(new DateOnly(2026, 6, 30));

        result.IsSuccess.ShouldBeTrue();
        membership.IsOpen.ShouldBeFalse();
        membership.EndDate.ShouldBe(new DateOnly(2026, 6, 30));
    }

    [Theory]
    [InlineData(UnionCoverageType.UnionApproved)]
    [InlineData(UnionCoverageType.Excluded)]
    public void AppliesDuesForPeriod_NonDuesCoverage_NeverApplies(UnionCoverageType coverage)
    {
        var membership = EmployeeUnionMembership.Create(TenantId, EmployeeId, Data(coverageType: coverage, duesValue: 0m)).Value;

        membership.AppliesDuesForPeriod(new DateOnly(2026, 6, 1)).ShouldBeFalse();
    }

    [Fact]
    public void AppliesDuesForPeriod_BeforeFirstEligibleMonth_ReturnsFalse()
    {
        // Notified 2026-01-15 -> first eligible period is 2026-02 (the month AFTER the notification's month).
        var membership = EmployeeUnionMembership.Create(TenantId, EmployeeId, Data(notifiedToEmployerAt: new DateOnly(2026, 1, 15))).Value;

        membership.AppliesDuesForPeriod(new DateOnly(2026, 1, 1)).ShouldBeFalse();
    }

    [Fact]
    public void AppliesDuesForPeriod_FirstEligibleMonthOnward_ReturnsTrue()
    {
        var membership = EmployeeUnionMembership.Create(TenantId, EmployeeId, Data(notifiedToEmployerAt: new DateOnly(2026, 1, 15))).Value;

        membership.AppliesDuesForPeriod(new DateOnly(2026, 2, 1)).ShouldBeTrue();
        membership.AppliesDuesForPeriod(new DateOnly(2026, 6, 1)).ShouldBeTrue();
    }

    [Fact]
    public void AppliesDuesForPeriod_AfterEndMonth_ReturnsFalse()
    {
        var membership = EmployeeUnionMembership.Create(
            TenantId, EmployeeId,
            Data(membershipDate: new DateOnly(2025, 12, 1), notifiedToEmployerAt: new DateOnly(2026, 1, 1), endDate: new DateOnly(2026, 5, 20))).Value;

        membership.AppliesDuesForPeriod(new DateOnly(2026, 5, 1)).ShouldBeTrue(); // end falls within this period's month
        membership.AppliesDuesForPeriod(new DateOnly(2026, 6, 1)).ShouldBeFalse();
    }
}

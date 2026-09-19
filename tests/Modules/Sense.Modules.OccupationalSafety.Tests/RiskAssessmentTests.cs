using Sense.Modules.OccupationalSafety.Domain;
using Sense.Modules.OccupationalSafety.Domain.HazardClassifications;
using Sense.Modules.OccupationalSafety.Domain.RiskAssessments;
using Shouldly;
using Xunit;

namespace Sense.Modules.OccupationalSafety.Tests;

public sealed class RiskAssessmentTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid LocationId = Guid.NewGuid();

    [Theory]
    [InlineData(HazardClass.Low, 72)]
    [InlineData(HazardClass.Medium, 48)]
    [InlineData(HazardClass.High, 24)]
    public void RenewalPeriods_MatchLegalMaximumsInMonths(HazardClass hazardClass, int expectedMonths)
    {
        RiskAssessmentRenewalPeriods.MonthsFor(hazardClass).ShouldBe(expectedMonths);
    }

    [Theory]
    [InlineData(HazardClass.Low, 6)]
    [InlineData(HazardClass.Medium, 4)]
    [InlineData(HazardClass.High, 2)]
    public void RenewalPeriods_MatchLegalMaximumsInYears(HazardClass hazardClass, int expectedYears)
    {
        (RiskAssessmentRenewalPeriods.MonthsFor(hazardClass) / 12).ShouldBe(expectedYears);
    }

    [Fact]
    public void Create_DerivesRenewalPeriodAndNextDueDate_FromHazardClass()
    {
        var result = RiskAssessment.Create(TenantId, LocationId, HazardClass.High, new DateOnly(2026, 3, 1), "ABC OSGB", null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.RenewalPeriodMonths.ShouldBe(24);
        result.Value.NextDueDate.ShouldBe(new DateOnly(2028, 3, 1));
    }

    [Fact]
    public void Create_WithEmptyLocationId_Fails()
    {
        var result = RiskAssessment.Create(TenantId, Guid.Empty, HazardClass.Low, new DateOnly(2026, 1, 1), "ABC OSGB", null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OhsDomainErrors.RiskAssessmentLocationRequired);
    }

    [Fact]
    public void Create_WithBlankPreparedBy_Fails()
    {
        var result = RiskAssessment.Create(TenantId, LocationId, HazardClass.Low, new DateOnly(2026, 1, 1), "   ", null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OhsDomainErrors.RiskAssessmentPreparedByRequired);
    }

    [Fact]
    public void RenewalPeriods_ThrowsForUndefinedHazardClass()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => RiskAssessmentRenewalPeriods.MonthsFor((HazardClass)99));
    }
}

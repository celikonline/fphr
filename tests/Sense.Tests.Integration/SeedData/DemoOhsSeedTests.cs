using Sense.Api.SeedData;
using Sense.Modules.OccupationalSafety.Domain.HazardClassifications;
using Shouldly;
using Xunit;

namespace Sense.Tests.Integration.SeedData;

/// <summary>
/// senseik-015 (İSG modülü, Faz 0-1) demo seed smoke test: card senseik-015's merge_gate is "demo seed: tehlike
/// sınıfı ve periyodlar yüklü" - covers <see cref="DataSeeder.CreateHazardClassificationOrThrow"/> and
/// <see cref="DataSeeder.CreateRiskAssessmentOrThrow"/> the way <c>SeedDemoOhs</c> calls them for the 3 demo
/// locations (İstanbul HQ/Ankara/İzmir), without needing a database.
/// </summary>
public sealed class DemoOhsSeedTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid LocationId = Guid.NewGuid();

    [Theory]
    [InlineData(HazardClass.Low)]
    [InlineData(HazardClass.Medium)]
    [InlineData(HazardClass.High)]
    public void CreateHazardClassificationOrThrow_BuildsAValidClassification_ForEveryHazardClass(HazardClass hazardClass)
    {
        var classification = DataSeeder.CreateHazardClassificationOrThrow(TenantId, LocationId, hazardClass, new DateOnly(2024, 1, 1), "not");

        classification.TenantId.ShouldBe(TenantId);
        classification.LocationId.ShouldBe(LocationId);
        classification.HazardClass.ShouldBe(hazardClass);
    }

    [Theory]
    [InlineData(HazardClass.Low, 72)]
    [InlineData(HazardClass.Medium, 48)]
    [InlineData(HazardClass.High, 24)]
    public void CreateRiskAssessmentOrThrow_DerivesTheRenewalPeriod_FromHazardClass(HazardClass hazardClass, int expectedMonths)
    {
        var assessment = DataSeeder.CreateRiskAssessmentOrThrow(TenantId, LocationId, hazardClass, new DateOnly(2024, 2, 1), "ABC OSGB", null);

        assessment.RenewalPeriodMonths.ShouldBe(expectedMonths);
        assessment.NextDueDate.ShouldBe(new DateOnly(2024, 2, 1).AddMonths(expectedMonths));
    }

    [Fact]
    public void CreateHazardClassificationOrThrow_WithInvalidLocation_Throws()
    {
        Should.Throw<InvalidOperationException>(() =>
            DataSeeder.CreateHazardClassificationOrThrow(TenantId, Guid.Empty, HazardClass.Low, new DateOnly(2024, 1, 1), null));
    }
}

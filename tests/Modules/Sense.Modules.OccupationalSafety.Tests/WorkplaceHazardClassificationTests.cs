using Sense.Modules.OccupationalSafety.Domain;
using Sense.Modules.OccupationalSafety.Domain.HazardClassifications;
using Shouldly;
using Xunit;

namespace Sense.Modules.OccupationalSafety.Tests;

public sealed class WorkplaceHazardClassificationTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid LocationId = Guid.NewGuid();

    [Fact]
    public void Create_WithEmptyLocationId_Fails()
    {
        var result = WorkplaceHazardClassification.Create(TenantId, Guid.Empty, HazardClass.Low, new DateOnly(2026, 1, 1), null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OhsDomainErrors.HazardClassificationLocationRequired);
    }

    [Fact]
    public void Create_WithInvalidHazardClass_Fails()
    {
        var result = WorkplaceHazardClassification.Create(TenantId, LocationId, (HazardClass)99, new DateOnly(2026, 1, 1), null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OhsDomainErrors.HazardClassificationInvalid);
    }

    [Fact]
    public void Create_WithValidData_Succeeds()
    {
        var result = WorkplaceHazardClassification.Create(TenantId, LocationId, HazardClass.High, new DateOnly(2026, 1, 1), "Çok tehlikeli - metal işleme");

        result.IsSuccess.ShouldBeTrue();
        result.Value.LocationId.ShouldBe(LocationId);
        result.Value.HazardClass.ShouldBe(HazardClass.High);
        result.Value.EffectiveFrom.ShouldBe(new DateOnly(2026, 1, 1));
        result.Value.Note.ShouldBe("Çok tehlikeli - metal işleme");
    }

    [Fact]
    public void Create_TrimsBlankNoteToNull()
    {
        var result = WorkplaceHazardClassification.Create(TenantId, LocationId, HazardClass.Low, new DateOnly(2026, 1, 1), "   ");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Note.ShouldBeNull();
    }
}

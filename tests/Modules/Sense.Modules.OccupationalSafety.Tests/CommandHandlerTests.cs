using NSubstitute;
using Sense.Modules.OccupationalSafety.Application.HazardClassifications;
using Sense.Modules.OccupationalSafety.Application.RiskAssessments;
using Sense.Modules.OccupationalSafety.Domain;
using Sense.Modules.OccupationalSafety.Domain.HazardClassifications;
using Sense.Shared.Contracts.Context;
using Shouldly;
using Xunit;

namespace Sense.Modules.OccupationalSafety.Tests;

public sealed class CommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid LocationId = Guid.NewGuid();

    private static ITenantContext TenantContext()
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        return tenant;
    }

    [Fact]
    public async Task SetHazardClassificationHandler_WithNewEffectiveDate_AddsClassification()
    {
        var repository = Substitute.For<IWorkplaceHazardClassificationRepository>();
        repository.ListHistoryAsync(LocationId, Arg.Any<CancellationToken>()).Returns(new List<WorkplaceHazardClassification>());
        var handler = new SetHazardClassificationHandler(TenantContext(), repository);

        var result = await handler.Handle(
            new SetHazardClassificationCommand(LocationId, HazardClass.High, new DateOnly(2026, 1, 1), "İlk atama"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        repository.Received(1).Add(Arg.Is<WorkplaceHazardClassification>(c => c.LocationId == LocationId && c.HazardClass == HazardClass.High));
    }

    [Fact]
    public async Task SetHazardClassificationHandler_WithDuplicateEffectiveDate_Fails()
    {
        var existing = WorkplaceHazardClassification.Create(TenantId, LocationId, HazardClass.Medium, new DateOnly(2026, 1, 1), null).Value;
        var repository = Substitute.For<IWorkplaceHazardClassificationRepository>();
        repository.ListHistoryAsync(LocationId, Arg.Any<CancellationToken>()).Returns(new List<WorkplaceHazardClassification> { existing });
        var handler = new SetHazardClassificationHandler(TenantContext(), repository);

        var result = await handler.Handle(
            new SetHazardClassificationCommand(LocationId, HazardClass.High, new DateOnly(2026, 1, 1), null), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OhsDomainErrors.HazardClassificationEffectiveDateDuplicate);
        repository.DidNotReceive().Add(Arg.Any<WorkplaceHazardClassification>());
    }

    [Fact]
    public async Task RecordRiskAssessmentHandler_WithoutHazardClassification_Fails()
    {
        var classifications = Substitute.For<IWorkplaceHazardClassificationRepository>();
        classifications.FindCurrentAsync(LocationId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns((WorkplaceHazardClassification?)null);
        var assessments = Substitute.For<IRiskAssessmentRepository>();
        var handler = new RecordRiskAssessmentHandler(TenantContext(), assessments, classifications);

        var result = await handler.Handle(
            new RecordRiskAssessmentCommand(LocationId, new DateOnly(2026, 1, 1), "ABC OSGB", null), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OhsDomainErrors.RiskAssessmentHazardClassificationMissing);
        assessments.DidNotReceive().Add(Arg.Any<Sense.Modules.OccupationalSafety.Domain.RiskAssessments.RiskAssessment>());
    }

    [Fact]
    public async Task RecordRiskAssessmentHandler_WithHazardClassification_SnapshotsClassAndDerivesRenewalPeriod()
    {
        var classification = WorkplaceHazardClassification.Create(TenantId, LocationId, HazardClass.Medium, new DateOnly(2025, 6, 1), null).Value;
        var classifications = Substitute.For<IWorkplaceHazardClassificationRepository>();
        classifications.FindCurrentAsync(LocationId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(classification);
        var assessments = Substitute.For<IRiskAssessmentRepository>();
        var handler = new RecordRiskAssessmentHandler(TenantContext(), assessments, classifications);

        var result = await handler.Handle(
            new RecordRiskAssessmentCommand(LocationId, new DateOnly(2026, 1, 1), "ABC OSGB", "Yıllık RD"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        assessments.Received(1).Add(Arg.Is<Sense.Modules.OccupationalSafety.Domain.RiskAssessments.RiskAssessment>(
            a => a.HazardClassAtAssessment == HazardClass.Medium && a.RenewalPeriodMonths == 48 && a.NextDueDate == new DateOnly(2030, 1, 1)));
    }
}

using NSubstitute;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Recruitment.Application.JobRequisitions;
using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Domain.JobRequisitions;
using Sense.Shared.Contracts.Context;
using Shouldly;
using Xunit;

namespace Sense.Modules.Recruitment.Tests;

/// <summary>
/// Department field customization (roadmap #87) applied inside <see cref="CreateJobRequisitionHandler"/>: a
/// department without a <see cref="RequisitionFieldTemplate"/> keeps the original fixed behavior, one with a
/// template can make Justification required or make EmploymentType/Reason optional (falling back to the
/// department's configured default).
/// </summary>
public sealed class CreateJobRequisitionHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _orgUnitId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();

    private CreateJobRequisitionHandler CreateHandler(IRequisitionFieldTemplateRepository fieldTemplates)
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(_tenantId);

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(_employeeId);

        var orgUnits = Substitute.For<IOrgUnitLookup>();
        orgUnits.ExistsActiveAsync(_tenantId, _orgUnitId, Arg.Any<CancellationToken>()).Returns(true);

        var requisitions = Substitute.For<IJobRequisitionRepository>();

        return new CreateJobRequisitionHandler(tenant, currentUser, requisitions, orgUnits, fieldTemplates);
    }

    private static IRequisitionFieldTemplateRepository NoTemplate()
    {
        var repo = Substitute.For<IRequisitionFieldTemplateRepository>();
        repo.FindByOrgUnitAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((RequisitionFieldTemplate?)null);
        return repo;
    }

    [Fact]
    public async Task NoDepartmentTemplate_EmploymentTypeAndReasonSupplied_JustificationOmitted_Succeeds()
    {
        var handler = CreateHandler(NoTemplate());

        var result = await handler.Handle(
            new CreateJobRequisitionCommand("Senior Backend Developer", _orgUnitId, "FullTime", 1, "NewPosition", Justification: null),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task NoDepartmentTemplate_EmploymentTypeOmitted_FailsWithOriginalFixedBehavior()
    {
        var handler = CreateHandler(NoTemplate());

        var result = await handler.Handle(
            new CreateJobRequisitionCommand("Senior Backend Developer", _orgUnitId, EmploymentType: null, 1, "NewPosition", Justification: null),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(RecruitmentDomainErrors.RequisitionEmploymentTypeRequired);
    }

    [Fact]
    public async Task DepartmentTemplateMakesJustificationRequired_JustificationOmitted_Fails()
    {
        var template = RequisitionFieldTemplate.Create(
            _tenantId, _orgUnitId,
            [new RequisitionFieldRuleInput(RequisitionFormField.Justification, IsVisible: true, IsRequired: true, DefaultValue: null)]).Value;
        var repo = Substitute.For<IRequisitionFieldTemplateRepository>();
        repo.FindByOrgUnitAsync(_orgUnitId, Arg.Any<CancellationToken>()).Returns(template);

        var handler = CreateHandler(repo);

        var result = await handler.Handle(
            new CreateJobRequisitionCommand("Senior Backend Developer", _orgUnitId, "FullTime", 1, "NewPosition", Justification: null),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(RecruitmentDomainErrors.RequisitionJustificationRequired);
    }

    [Fact]
    public async Task DepartmentTemplateMakesJustificationRequired_JustificationSupplied_Succeeds()
    {
        var template = RequisitionFieldTemplate.Create(
            _tenantId, _orgUnitId,
            [new RequisitionFieldRuleInput(RequisitionFormField.Justification, IsVisible: true, IsRequired: true, DefaultValue: null)]).Value;
        var repo = Substitute.For<IRequisitionFieldTemplateRepository>();
        repo.FindByOrgUnitAsync(_orgUnitId, Arg.Any<CancellationToken>()).Returns(template);

        var handler = CreateHandler(repo);

        var result = await handler.Handle(
            new CreateJobRequisitionCommand("Senior Backend Developer", _orgUnitId, "FullTime", 1, "NewPosition", "Team growth"),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task DepartmentTemplateMakesReasonOptionalWithDefault_ReasonOmitted_FallsBackToConfiguredDefault()
    {
        var template = RequisitionFieldTemplate.Create(
            _tenantId, _orgUnitId,
            [new RequisitionFieldRuleInput(RequisitionFormField.Reason, IsVisible: false, IsRequired: false, DefaultValue: nameof(RequisitionReason.Replacement))]).Value;
        var repo = Substitute.For<IRequisitionFieldTemplateRepository>();
        repo.FindByOrgUnitAsync(_orgUnitId, Arg.Any<CancellationToken>()).Returns(template);

        var requisitions = Substitute.For<IJobRequisitionRepository>();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(_tenantId);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(_employeeId);
        var orgUnits = Substitute.For<IOrgUnitLookup>();
        orgUnits.ExistsActiveAsync(_tenantId, _orgUnitId, Arg.Any<CancellationToken>()).Returns(true);

        var handler = new CreateJobRequisitionHandler(tenant, currentUser, requisitions, orgUnits, repo);

        var result = await handler.Handle(
            new CreateJobRequisitionCommand("Senior Backend Developer", _orgUnitId, "FullTime", 1, Reason: null, Justification: null),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        requisitions.Received(1).Add(Arg.Is<JobRequisition>(r => r.Reason == RequisitionReason.Replacement));
    }
}

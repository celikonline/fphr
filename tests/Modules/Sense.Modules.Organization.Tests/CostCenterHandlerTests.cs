using NSubstitute;
using Sense.Modules.Organization.Application.CostCenters;
using Sense.Modules.Organization.Domain;
using Sense.Modules.Organization.Domain.CostCenters;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Organization.Tests;

/// <summary>
/// Application-layer handler tests for the CostCenter catalog (S-4, roadmap Faz 2 item #2), mocking
/// <see cref="ICostCenterRepository"/> directly with NSubstitute - no Infrastructure/EF implementation
/// exists yet (see MIGRATION-ISTEGI.md, ADR-0005), so these mirror the fake-repo style already used by
/// Sense.Modules.Documents.Tests.DocumentTypeHandlerTests rather than the Testcontainers-backed
/// integration tests the rest of this module uses.
/// </summary>
public sealed class CostCenterHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static ITenantContext Tenant()
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        return tenant;
    }

    [Fact]
    public async Task Create_NewCode_Succeeds_AndAddsToRepository()
    {
        var repo = Substitute.For<ICostCenterRepository>();
        repo.CodeExistsAsync("IK-01", Arg.Any<CancellationToken>()).Returns(false);
        var handler = new CreateCostCenterHandler(Tenant(), repo);

        var result = await handler.Handle(new CreateCostCenterCommand("IK-01", "İnsan Kaynakları"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        repo.Received(1).Add(Arg.Is<CostCenter>(c => c.Code == "IK-01" && c.Name == "İnsan Kaynakları" && c.Id == result.Value.Id));
    }

    [Fact]
    public async Task Create_DuplicateCode_FailsWithConflict_AndDoesNotAdd()
    {
        var repo = Substitute.For<ICostCenterRepository>();
        repo.CodeExistsAsync("IK-01", Arg.Any<CancellationToken>()).Returns(true);
        var handler = new CreateCostCenterHandler(Tenant(), repo);

        var result = await handler.Handle(new CreateCostCenterCommand("IK-01", "İnsan Kaynakları"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OrganizationDomainErrors.CostCenterCodeTaken);
        repo.DidNotReceive().Add(Arg.Any<CostCenter>());
    }

    [Fact]
    public async Task Create_DuplicateCode_CaseInsensitive_FailsWithConflict()
    {
        // "ik-01" must be checked against the uppercase-normalized form (CostCenter.Create normalizes
        // Code to uppercase), otherwise a lowercase duplicate would slip past this check and only fail
        // later as an unhandled unique-index violation once the repository is EF-backed.
        var repo = Substitute.For<ICostCenterRepository>();
        repo.CodeExistsAsync("IK-01", Arg.Any<CancellationToken>()).Returns(true);
        var handler = new CreateCostCenterHandler(Tenant(), repo);

        var result = await handler.Handle(new CreateCostCenterCommand("ik-01", "İnsan Kaynakları"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OrganizationDomainErrors.CostCenterCodeTaken);
        repo.DidNotReceive().Add(Arg.Any<CostCenter>());
    }

    [Fact]
    public async Task Rename_ExistingCostCenter_ChangesName()
    {
        var costCenter = CostCenter.Create(TenantId, "IK-01", "İnsan Kaynakları").Value;
        var repo = Substitute.For<ICostCenterRepository>();
        repo.GetByIdAsync(costCenter.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(costCenter)));
        var handler = new RenameCostCenterHandler(repo);

        var result = await handler.Handle(new RenameCostCenterCommand(costCenter.Id, "İK ve İdari İşler"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        costCenter.Name.ShouldBe("İK ve İdari İşler");
    }

    [Fact]
    public async Task Rename_NotFound_ReturnsError()
    {
        var missingId = Guid.NewGuid();
        var repo = Substitute.For<ICostCenterRepository>();
        repo.GetByIdAsync(missingId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure<CostCenter>(Error.NotFound(OrganizationDomainErrors.CostCenterNotFound))));
        var handler = new RenameCostCenterHandler(repo);

        var result = await handler.Handle(new RenameCostCenterCommand(missingId, "Yeni Ad"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OrganizationDomainErrors.CostCenterNotFound);
    }

    [Fact]
    public async Task SetActive_ExistingCostCenter_Deactivates()
    {
        var costCenter = CostCenter.Create(TenantId, "IK-01", "İnsan Kaynakları").Value;
        var repo = Substitute.For<ICostCenterRepository>();
        repo.GetByIdAsync(costCenter.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(costCenter)));
        var handler = new SetCostCenterActiveHandler(repo);

        var result = await handler.Handle(new SetCostCenterActiveCommand(costCenter.Id, false), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        costCenter.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task SetActive_NotFound_ReturnsError()
    {
        var missingId = Guid.NewGuid();
        var repo = Substitute.For<ICostCenterRepository>();
        repo.GetByIdAsync(missingId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure<CostCenter>(Error.NotFound(OrganizationDomainErrors.CostCenterNotFound))));
        var handler = new SetCostCenterActiveHandler(repo);

        var result = await handler.Handle(new SetCostCenterActiveCommand(missingId, false), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OrganizationDomainErrors.CostCenterNotFound);
    }

    [Fact]
    public async Task SetActive_AlreadyInRequestedState_ReturnsRuleError()
    {
        var costCenter = CostCenter.Create(TenantId, "IK-01", "İnsan Kaynakları").Value;
        var repo = Substitute.For<ICostCenterRepository>();
        repo.GetByIdAsync(costCenter.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(costCenter)));
        var handler = new SetCostCenterActiveHandler(repo);

        var result = await handler.Handle(new SetCostCenterActiveCommand(costCenter.Id, true), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OrganizationDomainErrors.CostCenterAlreadyActive);
    }

    [Fact]
    public async Task List_ActiveOnly_ReturnsSortedByCode()
    {
        var b = CostCenter.Create(TenantId, "B-02", "Satış").Value;
        var a = CostCenter.Create(TenantId, "A-01", "İnsan Kaynakları").Value;
        var repo = Substitute.For<ICostCenterRepository>();
        repo.ListAllAsync(activeOnly: true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CostCenter>>([b, a]));
        var handler = new ListCostCentersHandler(repo);

        var result = await handler.Handle(new ListCostCentersQuery(IncludeInactive: false), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(c => c.Code).ShouldBe(["A-01", "B-02"]);
    }

    [Fact]
    public async Task List_IncludeInactive_PassesActiveOnlyFalse()
    {
        var repo = Substitute.For<ICostCenterRepository>();
        repo.ListAllAsync(activeOnly: false, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CostCenter>>([]));
        var handler = new ListCostCentersHandler(repo);

        var result = await handler.Handle(new ListCostCentersQuery(IncludeInactive: true), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await repo.Received(1).ListAllAsync(activeOnly: false, Arg.Any<CancellationToken>());
    }
}

using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Organization.Application;
using Sense.Modules.Organization.Application.OrgUnits;
using Sense.Modules.Organization.Domain;
using Sense.Modules.Organization.Domain.OrgUnits;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Organization.Tests;

/// <summary>
/// Application-layer handler tests for <see cref="DeactivateOrgUnitHandler"/> (TALIMAT gün açılışı tur 1
/// BE md.1-2): the child-unit check and the active-employee check (new, mirrors
/// <c>DeleteOrgUnitTypeHandler</c>'s <see cref="IEmployeeLookup.AnyActiveInOrgUnitsAsync"/> use), mocking
/// <see cref="IOrgUnitRepository"/> and <see cref="IEmployeeLookup"/> with NSubstitute (fake-repo style,
/// same as <c>CostCenterHandlerTests</c> - no Testcontainers needed for pure handler logic).
/// </summary>
public sealed class DeactivateOrgUnitHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static ITenantContext Tenant()
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        return tenant;
    }

    private static OrgUnit NewUnit() => OrgUnit.Create(TenantId, Guid.NewGuid(), null, "Satış").Value;

    [Fact]
    public async Task Deactivate_HasActiveChildren_FailsWithConflict_AndDoesNotCheckEmployees()
    {
        var unit = NewUnit();
        var units = Substitute.For<IOrgUnitRepository>();
        units.GetByIdAsync(unit.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(unit)));
        units.HasChildrenAsync(unit.Id, Arg.Any<CancellationToken>()).Returns(true);
        var employeeLookup = Substitute.For<IEmployeeLookup>();
        var handler = new DeactivateOrgUnitHandler(Tenant(), units, employeeLookup);

        var result = await handler.Handle(new DeactivateOrgUnitCommand(unit.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OrganizationErrors.HasActiveChildren);
        unit.IsActive.ShouldBeTrue();
        await employeeLookup.DidNotReceiveWithAnyArgs()
            .AnyActiveInOrgUnitsAsync(default, default!, default);
    }

    [Fact]
    public async Task Deactivate_HasActiveEmployees_FailsWithConflict()
    {
        var unit = NewUnit();
        var units = Substitute.For<IOrgUnitRepository>();
        units.GetByIdAsync(unit.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(unit)));
        units.HasChildrenAsync(unit.Id, Arg.Any<CancellationToken>()).Returns(false);
        var employeeLookup = Substitute.For<IEmployeeLookup>();
        employeeLookup.AnyActiveInOrgUnitsAsync(TenantId, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == unit.Id), Arg.Any<CancellationToken>())
            .Returns(true);
        var handler = new DeactivateOrgUnitHandler(Tenant(), units, employeeLookup);

        var result = await handler.Handle(new DeactivateOrgUnitCommand(unit.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OrganizationErrors.UnitHasActiveEmployees);
        unit.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task Deactivate_NoChildrenNoEmployees_Succeeds()
    {
        var unit = NewUnit();
        var units = Substitute.For<IOrgUnitRepository>();
        units.GetByIdAsync(unit.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(unit)));
        units.HasChildrenAsync(unit.Id, Arg.Any<CancellationToken>()).Returns(false);
        var employeeLookup = Substitute.For<IEmployeeLookup>();
        employeeLookup.AnyActiveInOrgUnitsAsync(TenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(false);
        var handler = new DeactivateOrgUnitHandler(Tenant(), units, employeeLookup);

        var result = await handler.Handle(new DeactivateOrgUnitCommand(unit.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        unit.IsActive.ShouldBeFalse();
    }
}

using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Organization.Application.CostCenters;
using Sense.Modules.Organization.Domain;
using Sense.Modules.Organization.Domain.CostCenters;
using Sense.Modules.Organization.Domain.OrgUnits;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Organization.Tests;

public sealed class EmployeeCostCenterAssignmentCompatibilityTests
{
    [Fact]
    public async Task AssignmentDto_UsesSamePrecedenceAndPreservesLegacyFields()
    {
        var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); var directId = Guid.NewGuid(); var legacyId = Guid.NewGuid(); var inheritedId = Guid.NewGuid();
        var tenant = Substitute.For<ITenantContext>(); tenant.TenantId.Returns(tenantId);
        var employees = Substitute.For<IEmployeeLookup>(); employees.ExistsActiveAsync(tenantId, employeeId, Arg.Any<CancellationToken>()).Returns(true);
        var unit = OrgUnit.Create(tenantId, Guid.NewGuid(), null, "Unit").Value; unit.AssignCostCenter(inheritedId);
        employees.GetOrgUnitIdAsync(tenantId, employeeId, Arg.Any<CancellationToken>()).Returns(unit.Id);
        var units = Substitute.For<IOrgUnitRepository>(); units.GetByIdAsync(unit.Id, Arg.Any<CancellationToken>()).Returns(Result.Success(unit));
        var overrides = Substitute.For<IEmployeeCostCenterOverrideRepository>();
        overrides.FindByEmployeeIdAsync(employeeId, Arg.Any<CancellationToken>()).Returns(EmployeeCostCenterOverride.Create(tenantId, employeeId, legacyId));
        var direct = Substitute.For<IEmployeeCostCenterLookup>();
        direct.GetAsync(tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, Guid> { [employeeId] = directId });
        var handler = new GetEmployeeCostCenterAssignmentHandler(tenant, employees, units, overrides, direct);
        var result = (await handler.Handle(new GetEmployeeCostCenterAssignmentQuery(employeeId), CancellationToken.None)).Value;
        result.EmployeeDirectCostCenterId.ShouldBe(directId); result.EmployeeOverrideCostCenterId.ShouldBe(legacyId); result.OrgUnitCostCenterId.ShouldBe(inheritedId);
        result.EffectiveCostCenterId.ShouldBe(directId); result.Source.ShouldBe("employeeDirect");
        direct.GetAsync(tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, Guid>());
        result = (await handler.Handle(new GetEmployeeCostCenterAssignmentQuery(employeeId), CancellationToken.None)).Value;
        result.EffectiveCostCenterId.ShouldBe(legacyId); result.Source.ShouldBe("employee");
        overrides.FindByEmployeeIdAsync(employeeId, Arg.Any<CancellationToken>()).Returns((EmployeeCostCenterOverride?)null);
        result = (await handler.Handle(new GetEmployeeCostCenterAssignmentQuery(employeeId), CancellationToken.None)).Value;
        result.EffectiveCostCenterId.ShouldBe(inheritedId); result.Source.ShouldBe("orgUnit");
        unit.AssignCostCenter(null);
        result = (await handler.Handle(new GetEmployeeCostCenterAssignmentQuery(employeeId), CancellationToken.None)).Value;
        result.EffectiveCostCenterId.ShouldBeNull(); result.Source.ShouldBe("unassigned");
    }
}

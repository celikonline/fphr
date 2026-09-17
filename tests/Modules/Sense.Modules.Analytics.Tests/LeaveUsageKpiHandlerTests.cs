using NSubstitute;
using Sense.Modules.Analytics.Application.Kpis;
using Sense.Modules.Analytics.Contracts;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Leave.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;

namespace Sense.Modules.Analytics.Tests;

/// <summary>
/// GetLeaveUsageKpiHandler (S-15): usage-rate math and IDataScopeService pass-through, against a fake
/// ILeaveUsageReader (S-18, implemented/tested separately in Leave.Tests).
/// </summary>
public sealed class LeaveUsageKpiHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly ILeaveUsageReader _leaveUsage = Substitute.For<ILeaveUsageReader>();
    private readonly IEmployeeDirectory _employeeDirectory = Substitute.For<IEmployeeDirectory>();
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();
    private readonly ITenantContext _tenant;

    public LeaveUsageKpiHandlerTests()
    {
        _tenant = Substitute.For<ITenantContext>();
        _tenant.TenantId.Returns(_tenantId);
    }

    private GetLeaveUsageKpiHandler CreateHandler() => new(_tenant, _leaveUsage, _employeeDirectory, _dataScope, TimeProvider.System);

    [Fact]
    public async Task Handle_ComputesUsageRate_FromEntitledAndUsedDays()
    {
        _dataScope.GetVisibleEmployeeIdsAsync(AnalyticsPermissions.KpiView.Key, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)null);
        var typeId = Guid.NewGuid();
        _leaveUsage.GetAnnualUsageAsync(_tenantId, 2026, null, Arg.Any<CancellationToken>())
            .Returns(new LeaveUsageSummary(20m, 5m, [new LeaveTypeUsage(typeId, "Yıllık İzin", 5m)]));

        var handler = CreateHandler();
        var result = await handler.Handle(new GetLeaveUsageKpiQuery(2026), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalEntitledDays.ShouldBe(20m);
        result.Value.UsedDays.ShouldBe(5m);
        result.Value.UsageRate.ShouldBe(0.25m);
        result.Value.ByLeaveType.Single().LeaveTypeName.ShouldBe("Yıllık İzin");
    }

    [Fact]
    public async Task Handle_UsageRateIsZero_WhenNoEntitlement()
    {
        _dataScope.GetVisibleEmployeeIdsAsync(AnalyticsPermissions.KpiView.Key, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)null);
        _leaveUsage.GetAnnualUsageAsync(_tenantId, 2026, null, Arg.Any<CancellationToken>())
            .Returns(new LeaveUsageSummary(0m, 0m, []));

        var handler = CreateHandler();
        var result = await handler.Handle(new GetLeaveUsageKpiQuery(2026), CancellationToken.None);

        result.Value.UsageRate.ShouldBe(0m);
    }

    [Fact]
    public async Task Handle_PassesDataScopeVisibleEmployeeIds_ToLeaveUsageReader_ForRestrictedCaller()
    {
        var visible = new HashSet<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        _dataScope.GetVisibleEmployeeIdsAsync(AnalyticsPermissions.KpiView.Key, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)visible);
        _leaveUsage.GetAnnualUsageAsync(_tenantId, 2026, visible, Arg.Any<CancellationToken>())
            .Returns(new LeaveUsageSummary(10m, 2m, []));

        var handler = CreateHandler();
        var result = await handler.Handle(new GetLeaveUsageKpiQuery(2026), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await _leaveUsage.Received(1).GetAnnualUsageAsync(_tenantId, 2026, visible, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OrgUnitId_NarrowsToUnitEmployeeIds_ViaEmployeeDirectory_ForCompanyScopeCaller()
    {
        var orgUnitId = Guid.NewGuid();
        var unitEmployeeIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        _dataScope.GetVisibleEmployeeIdsAsync(AnalyticsPermissions.KpiView.Key, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)null);
        _employeeDirectory.ListActiveEmployeeIdsInOrgUnitsAsync(_tenantId, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { orgUnitId })), Arg.Any<CancellationToken>())
            .Returns(unitEmployeeIds);
        _employeeDirectory.ListActiveEmployeeIdsAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(unitEmployeeIds);
        _leaveUsage.GetAnnualUsageAsync(_tenantId, 2026, Arg.Is<IReadOnlyCollection<Guid>?>(ids => ids != null && ids.SequenceEqual(unitEmployeeIds)), Arg.Any<CancellationToken>())
            .Returns(new LeaveUsageSummary(8m, 1m, []));

        var handler = CreateHandler();
        var result = await handler.Handle(new GetLeaveUsageKpiQuery(2026, orgUnitId), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalEntitledDays.ShouldBe(8m);
    }

    /// <summary>G-10-16: the org-unit branch must only count employees that are also in the tenant's Active set
    /// (<c>ListActiveEmployeeIdsInOrgUnitsAsync</c> is broader - it excludes only Terminated, so a Suspended
    /// employee id would otherwise slip in and unit totals would stop matching the company-wide total).</summary>
    [Fact]
    public async Task Handle_OrgUnitId_ExcludesEmployeeNotInActiveSet_ForCompanyScopeCaller()
    {
        var orgUnitId = Guid.NewGuid();
        var activeEmployeeId = Guid.NewGuid();
        var suspendedEmployeeId = Guid.NewGuid();
        _dataScope.GetVisibleEmployeeIdsAsync(AnalyticsPermissions.KpiView.Key, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)null);
        _employeeDirectory.ListActiveEmployeeIdsInOrgUnitsAsync(_tenantId, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { orgUnitId })), Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { activeEmployeeId, suspendedEmployeeId });
        _employeeDirectory.ListActiveEmployeeIdsAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { activeEmployeeId });
        _leaveUsage.GetAnnualUsageAsync(_tenantId, 2026, Arg.Is<IReadOnlyCollection<Guid>?>(ids => ids != null && ids.SequenceEqual(new[] { activeEmployeeId })), Arg.Any<CancellationToken>())
            .Returns(new LeaveUsageSummary(4m, 2m, []));

        var handler = CreateHandler();
        var result = await handler.Handle(new GetLeaveUsageKpiQuery(2026, orgUnitId), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await _leaveUsage.Received(1).GetAnnualUsageAsync(_tenantId, 2026, Arg.Is<IReadOnlyCollection<Guid>?>(ids => ids != null && ids.SequenceEqual(new[] { activeEmployeeId })), Arg.Any<CancellationToken>());
    }
}

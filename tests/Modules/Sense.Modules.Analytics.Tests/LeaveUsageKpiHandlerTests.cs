using NSubstitute;
using Sense.Modules.Analytics.Application.Kpis;
using Sense.Modules.Analytics.Contracts;
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
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();
    private readonly ITenantContext _tenant;

    public LeaveUsageKpiHandlerTests()
    {
        _tenant = Substitute.For<ITenantContext>();
        _tenant.TenantId.Returns(_tenantId);
    }

    private GetLeaveUsageKpiHandler CreateHandler() => new(_tenant, _leaveUsage, _dataScope, TimeProvider.System);

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
}

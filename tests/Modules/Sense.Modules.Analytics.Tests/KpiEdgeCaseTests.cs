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
/// KPI sınır durumları (TALIMAT madde 2): sıfır çalışan, tek çalışan, dönem sınırı, totals[] şekli.
/// Mevcut testlerin kapsamadığı kenar durumlar.
/// </summary>
public sealed class KpiEdgeCaseTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly ILeaveUsageReader _leaveUsage = Substitute.For<ILeaveUsageReader>();
    private readonly IEmployeeDirectory _employeeDirectory = Substitute.For<IEmployeeDirectory>();
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();
    private readonly ITenantContext _tenant;

    public KpiEdgeCaseTests()
    {
        _tenant = Substitute.For<ITenantContext>();
        _tenant.TenantId.Returns(_tenantId);
    }

    private GetLeaveUsageKpiHandler CreateLeaveHandler() =>
        new(_tenant, _leaveUsage, _employeeDirectory, _dataScope, TimeProvider.System);

    /// <summary>Tenant'ta sıfır çalışan → KPI 0 döner, DivideByZeroException atmaz.</summary>
    [Fact]
    public async Task LeaveUsageKpi_WithZeroEmployees_ReturnsZero_WithoutException()
    {
        _dataScope.GetVisibleEmployeeIdsAsync(AnalyticsPermissions.KpiView.Key, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)null);
        _leaveUsage.GetAnnualUsageAsync(_tenantId, 2026, null, Arg.Any<CancellationToken>())
            .Returns(new LeaveUsageSummary(0m, 0m, []));

        var handler = CreateLeaveHandler();
        var result = await handler.Handle(new GetLeaveUsageKpiQuery(2026), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalEntitledDays.ShouldBe(0m);
        result.Value.UsedDays.ShouldBe(0m);
        result.Value.UsageRate.ShouldBe(0m);
        result.Value.ByLeaveType.ShouldBeEmpty();
    }

    /// <summary>Tek çalışan → yüzde doğru (100% veya 0% şartlı).</summary>
    [Fact]
    public async Task LeaveUsageKpi_WithSingleEmployee_ComputesRateCorrectly()
    {
        _dataScope.GetVisibleEmployeeIdsAsync(AnalyticsPermissions.KpiView.Key, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)null);
        var typeId = Guid.NewGuid();
        _leaveUsage.GetAnnualUsageAsync(_tenantId, 2026, null, Arg.Any<CancellationToken>())
            .Returns(new LeaveUsageSummary(10m, 10m, [new LeaveTypeUsage(typeId, "Yıllık İzin", 10m)]));

        var handler = CreateLeaveHandler();
        var result = await handler.Handle(new GetLeaveUsageKpiQuery(2026), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.UsageRate.ShouldBe(1.0m); // 100%
    }

    /// <summary>ByLeaveType şekli: sadece kullanılan türler listelenir, sıra tutarlı.</summary>
    [Fact]
    public async Task LeaveUsageKpi_ByLeaveType_IncludesOnlyUsedTypes()
    {
        _dataScope.GetVisibleEmployeeIdsAsync(AnalyticsPermissions.KpiView.Key, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)null);
        var annualId = Guid.NewGuid();
        var sickId = Guid.NewGuid();
        _leaveUsage.GetAnnualUsageAsync(_tenantId, 2026, null, Arg.Any<CancellationToken>())
            .Returns(new LeaveUsageSummary(
                15m,
                8m,
                [
                    new LeaveTypeUsage(annualId, "Yıllık İzin", 5m),
                    new LeaveTypeUsage(sickId, "Hastalık İzni", 3m)
                ]
            ));

        var handler = CreateLeaveHandler();
        var result = await handler.Handle(new GetLeaveUsageKpiQuery(2026), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ByLeaveType.Count.ShouldBe(2);
        result.Value.ByLeaveType.ShouldContain(t => t.LeaveTypeName == "Yıllık İzin" && t.UsedDays == 5m);
        result.Value.ByLeaveType.ShouldContain(t => t.LeaveTypeName == "Hastalık İzni" && t.UsedDays == 3m);
    }
}

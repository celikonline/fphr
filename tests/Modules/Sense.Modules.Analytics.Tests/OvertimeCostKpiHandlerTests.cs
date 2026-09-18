using NSubstitute;
using Sense.Modules.Analytics.Application.Kpis;
using Sense.Modules.Analytics.Contracts;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;

namespace Sense.Modules.Analytics.Tests;

/// <summary>
/// GetOvertimeCostKpiHandler (S-17, tur içi ek iş md.7): IDataScopeService/IEmployeeDirectory wiring and the
/// attendance-summary -&gt; salary -&gt; OvertimeCostCalculator pipeline, against fakes.
/// </summary>
public sealed class OvertimeCostKpiHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly IAttendanceMonthlySummaryReader _attendanceSummaries = Substitute.For<IAttendanceMonthlySummaryReader>();
    private readonly IEmployeeSalaryLookup _salaryLookup = Substitute.For<IEmployeeSalaryLookup>();
    private readonly IEmployeeDirectory _employeeDirectory = Substitute.For<IEmployeeDirectory>();
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();
    private readonly ITenantContext _tenant;

    public OvertimeCostKpiHandlerTests()
    {
        _tenant = Substitute.For<ITenantContext>();
        _tenant.TenantId.Returns(_tenantId);
    }

    private GetOvertimeCostKpiHandler CreateHandler() =>
        new(_tenant, _attendanceSummaries, _salaryLookup, _employeeDirectory, _dataScope);

    /// <summary>Maaş hassas alan - hem analytics.kpi.view hem employee.salary.view gerekir (AuthorizationBehaviour AND,
    /// tam pipeline testi AuthorizationBehaviourPlatformTests emsalinde; burada sözleşmenin iki izni de taşıdığını doğrular).</summary>
    [Fact]
    public void Query_RequiresBothKpiViewAndSalaryViewPermissions()
    {
        var permissions = typeof(GetOvertimeCostKpiQuery)
            .GetCustomAttributes(typeof(RequiresPermissionAttribute), inherit: true)
            .Cast<RequiresPermissionAttribute>()
            .Select(a => a.Permission)
            .ToList();

        permissions.ShouldContain(AnalyticsPermissions.KpiView.Key);
        permissions.ShouldContain(EmployeePermissions.SalaryViewKey);
    }

    [Fact]
    public async Task Handle_CompanyScope_UsesEmployeeDirectory_ForEmployeeIds_AndComputesCost()
    {
        var employeeId = Guid.NewGuid();
        _dataScope.GetVisibleEmployeeIdsAsync(AnalyticsPermissions.KpiView.Key, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)null);
        _employeeDirectory.ListActiveEmployeeIdsAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { employeeId });
        _attendanceSummaries.GetAsync(_tenantId, 2026, 9, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { employeeId })), Arg.Any<CancellationToken>())
            .Returns(new List<AttendanceMonthlySummary> { new(employeeId, 20m, 160m, 0m, 10m, false) });
        _salaryLookup.GetCurrentSalariesAsync(_tenantId, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { employeeId })), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, EmployeeCurrentSalary>
            {
                [employeeId] = new(22_500m, "TRY", EmployeeSalaryType.Gross, EmployeePayFrequency.Monthly, new DateOnly(2026, 1, 1)),
            });

        var handler = CreateHandler();
        var result = await handler.Handle(new GetOvertimeCostKpiQuery(2026, 9), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Year.ShouldBe(2026);
        result.Value.Month.ShouldBe(9);
        result.Value.EmployeeCount.ShouldBe(1);
        result.Value.ExcludedEmployeeCount.ShouldBe(0);
        result.Value.Totals.ShouldContain(t => t.Currency == "TRY" && t.Amount == 1500m); // 22500/225 * 1.5 * 10
        await _employeeDirectory.Received(1).ListActiveEmployeeIdsAsync(_tenantId, Arg.Any<CancellationToken>());
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
        _attendanceSummaries.GetAsync(_tenantId, 2026, 9, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { activeEmployeeId })), Arg.Any<CancellationToken>())
            .Returns(new List<AttendanceMonthlySummary> { new(activeEmployeeId, 20m, 160m, 0m, 5m, false) });
        _salaryLookup.GetCurrentSalariesAsync(_tenantId, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { activeEmployeeId })), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, EmployeeCurrentSalary>
            {
                [activeEmployeeId] = new(22_500m, "TRY", EmployeeSalaryType.Gross, EmployeePayFrequency.Monthly, new DateOnly(2026, 1, 1)),
            });

        var handler = CreateHandler();
        var result = await handler.Handle(new GetOvertimeCostKpiQuery(2026, 9, orgUnitId), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.EmployeeCount.ShouldBe(1);
        // G-10-19: the attendance summary itself already excludes the suspended employee (asserted above via the
        // GetAsync Arg.Is match) - the batched salary lookup is only ever called with that same, already-filtered set.
        await _salaryLookup.DidNotReceive().GetCurrentSalariesAsync(_tenantId, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(suspendedEmployeeId)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoVisibleEmployees_ReturnsEmptyResult_WithoutCallingAttendanceReader()
    {
        var visible = new HashSet<Guid>();
        _dataScope.GetVisibleEmployeeIdsAsync(AnalyticsPermissions.KpiView.Key, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)visible);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetOvertimeCostKpiQuery(2026, 9), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Totals.ShouldBeEmpty();
        result.Value.EmployeeCount.ShouldBe(0);
        await _attendanceSummaries.DidNotReceiveWithAnyArgs().GetAsync(default, default, default, default!, default);
    }
}

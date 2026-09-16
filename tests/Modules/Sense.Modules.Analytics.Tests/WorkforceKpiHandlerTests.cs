using NSubstitute;
using Sense.Modules.Analytics.Application.Kpis;
using Sense.Modules.Analytics.Contracts;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;

namespace Sense.Modules.Analytics.Tests;

/// <summary>
/// GetWorkforceKpisHandler: ratio/share math and defaulting, against fake IEmployeeAnalyticsQueries/IOrgUnitLookup.
/// No DB - Employee's own EmployeeAnalyticsQueries implementation is covered separately in Employee.Tests.
/// </summary>
public sealed class WorkforceKpiHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly IEmployeeAnalyticsQueries _employeeAnalytics = Substitute.For<IEmployeeAnalyticsQueries>();
    private readonly IOrgUnitLookup _orgUnits = Substitute.For<IOrgUnitLookup>();
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();
    private readonly ITenantContext _tenant;

    public WorkforceKpiHandlerTests()
    {
        _tenant = Substitute.For<ITenantContext>();
        _tenant.TenantId.Returns(_tenantId);
        // Defaults, overridden by the tests that care about them.
        _employeeAnalytics.GetTerminationCountsAsync(_tenantId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new TerminationCounts(0, 0));
        _employeeAnalytics.GetGenderByOrgUnitAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new List<GenderByOrgUnitCount>());
        _orgUnits.ListActiveUnitsAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new List<OrgUnitInfo>());
        // null = Company scope (unrestricted) - the existing tests below all assume the full 5-KPI set; the
        // narrower-scope (unit manager) path is covered separately in GenderRatioByUnit_RestrictedScope_*.
        _dataScope.GetAccessibleOrgUnitIdsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((IReadOnlySet<Guid>?)null);
    }

    private GetWorkforceKpisHandler CreateHandler() => new(_tenant, _employeeAnalytics, _orgUnits, _dataScope, TimeProvider.System);

    [Fact]
    public async Task CareerPathRatio_DividesChangesByActiveHeadcount()
    {
        _employeeAnalytics.GetActiveHeadcountAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(50);
        _employeeAnalytics.GetCareerPathChangeCountsAsync(_tenantId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new CareerPathChangeCounts(Promotion: 3, LateralMove: 2, Demotion: 0, TitleChange: 0));
        _employeeAnalytics.GetGenderHeadcountAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int>());

        var handler = CreateHandler();
        var result = await handler.Handle(
            new GetWorkforceKpisQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var ratio = result.Value.Single(k => k.Key == KpiKeys.CareerPathRatio);
        ratio.Value.ShouldBe(0.1m); // 5 changes / 50 headcount
        ratio.Breakdown.Single(b => b.Dimension == nameof(CareerPathChangeCounts.Promotion)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task CareerPathRatio_IsZero_WhenNoActiveEmployees()
    {
        _employeeAnalytics.GetActiveHeadcountAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(0);
        _employeeAnalytics.GetCareerPathChangeCountsAsync(_tenantId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new CareerPathChangeCounts(0, 0, 0, 0));
        _employeeAnalytics.GetGenderHeadcountAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int>());

        var handler = CreateHandler();
        var result = await handler.Handle(
            new GetWorkforceKpisQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)), CancellationToken.None);

        result.Value.Single(k => k.Key == KpiKeys.CareerPathRatio).Value.ShouldBe(0m);
    }

    [Fact]
    public async Task GenderRatio_ReturnsTotalHeadcountAndShares()
    {
        _employeeAnalytics.GetActiveHeadcountAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(10);
        _employeeAnalytics.GetCareerPathChangeCountsAsync(_tenantId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new CareerPathChangeCounts(0, 0, 0, 0));
        _employeeAnalytics.GetGenderHeadcountAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int> { ["Female"] = 4, ["Male"] = 6 });

        var handler = CreateHandler();
        var result = await handler.Handle(
            new GetWorkforceKpisQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)), CancellationToken.None);

        var gender = result.Value.Single(k => k.Key == KpiKeys.GenderRatio);
        gender.Value.ShouldBe(10m);
        gender.Breakdown.Single(b => b.Dimension == "Male").Share.ShouldBe(0.6m);
        gender.Breakdown.Single(b => b.Dimension == "Female").Share.ShouldBe(0.4m);
    }

    [Fact]
    public async Task TurnoverRate_DividesTerminationsByActiveHeadcount_AndEarlyTurnoverIsShareWithinSixMonths()
    {
        _employeeAnalytics.GetActiveHeadcountAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(40);
        _employeeAnalytics.GetCareerPathChangeCountsAsync(_tenantId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new CareerPathChangeCounts(0, 0, 0, 0));
        _employeeAnalytics.GetGenderHeadcountAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int>());
        _employeeAnalytics.GetTerminationCountsAsync(_tenantId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new TerminationCounts(Total: 4, WithinFirstSixMonths: 1));

        var handler = CreateHandler();
        var result = await handler.Handle(
            new GetWorkforceKpisQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)), CancellationToken.None);

        result.Value.Single(k => k.Key == KpiKeys.TurnoverRate).Value.ShouldBe(0.1m); // 4 / 40
        result.Value.Single(k => k.Key == KpiKeys.EarlyTurnoverRate).Value.ShouldBe(0.25m); // 1 / 4
    }

    [Fact]
    public async Task TurnoverRate_IsZero_WhenNoActiveEmployees()
    {
        _employeeAnalytics.GetActiveHeadcountAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(0);
        _employeeAnalytics.GetCareerPathChangeCountsAsync(_tenantId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new CareerPathChangeCounts(0, 0, 0, 0));
        _employeeAnalytics.GetGenderHeadcountAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int>());
        _employeeAnalytics.GetTerminationCountsAsync(_tenantId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new TerminationCounts(0, 0));

        var handler = CreateHandler();
        var result = await handler.Handle(
            new GetWorkforceKpisQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)), CancellationToken.None);

        result.Value.Single(k => k.Key == KpiKeys.TurnoverRate).Value.ShouldBe(0m);
        result.Value.Single(k => k.Key == KpiKeys.EarlyTurnoverRate).Value.ShouldBe(0m);
    }

    [Fact]
    public async Task GenderRatioByUnit_LabelsKnownUnits_AndFallsBackToUnassignedForNullOrgUnitId()
    {
        var unitId = Guid.NewGuid();
        _employeeAnalytics.GetActiveHeadcountAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(3);
        _employeeAnalytics.GetCareerPathChangeCountsAsync(_tenantId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new CareerPathChangeCounts(0, 0, 0, 0));
        _employeeAnalytics.GetGenderHeadcountAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int>());
        _employeeAnalytics.GetGenderByOrgUnitAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new List<GenderByOrgUnitCount>
            {
                new(unitId, "Female", 2),
                new(null, "Male", 1),
            });
        _orgUnits.ListActiveUnitsAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new List<OrgUnitInfo> { new(unitId, Guid.NewGuid(), null, "Ankara Şubesi") });

        var handler = CreateHandler();
        var result = await handler.Handle(
            new GetWorkforceKpisQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)), CancellationToken.None);

        var byUnit = result.Value.Single(k => k.Key == KpiKeys.GenderRatioByUnit);
        byUnit.Value.ShouldBe(3m);
        byUnit.Breakdown.Single(b => b.Dimension == "Ankara Şubesi / Female").Share.ShouldBe(0.6667m);
        byUnit.Breakdown.Single(b => b.Dimension == "Unassigned / Male").Count.ShouldBe(1);
    }

    [Fact]
    public async Task Period_DefaultsToCurrentCalendarYear_WhenNotGiven()
    {
        _employeeAnalytics.GetActiveHeadcountAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(1);
        _employeeAnalytics.GetCareerPathChangeCountsAsync(_tenantId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new CareerPathChangeCounts(0, 0, 0, 0));
        _employeeAnalytics.GetGenderHeadcountAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int>());

        var handler = CreateHandler();
        var result = await handler.Handle(new GetWorkforceKpisQuery(), CancellationToken.None);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        result.Value[0].PeriodStart.ShouldBe(new DateOnly(today.Year, 1, 1));
        result.Value[0].PeriodEnd.ShouldBe(today);
    }

    [Fact]
    public async Task RestrictedScope_OnlyReturnsGenderRatioByUnit_FilteredToAccessibleUnits()
    {
        var accessibleUnit = Guid.NewGuid();
        var otherUnit = Guid.NewGuid();
        _dataScope.GetAccessibleOrgUnitIdsAsync(AnalyticsPermissions.KpiView.Key, Arg.Any<CancellationToken>())
            .Returns(new HashSet<Guid> { accessibleUnit });
        _employeeAnalytics.GetGenderByOrgUnitAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new List<GenderByOrgUnitCount>
            {
                new(accessibleUnit, "Female", 2),
                new(otherUnit, "Male", 5),
            });
        _orgUnits.ListActiveUnitsAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new List<OrgUnitInfo> { new(accessibleUnit, Guid.NewGuid(), null, "Erişilebilir Birim") });

        var handler = CreateHandler();
        var result = await handler.Handle(
            new GetWorkforceKpisQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)), CancellationToken.None);

        // Only the unit breakdown KPI - the four tenant-wide aggregates are excluded rather than shown unscoped.
        result.Value.Count.ShouldBe(1);
        var byUnit = result.Value.Single(k => k.Key == KpiKeys.GenderRatioByUnit);
        byUnit.Value.ShouldBe(2m);
        byUnit.Breakdown.ShouldHaveSingleItem();
        byUnit.Breakdown[0].Dimension.ShouldBe("Erişilebilir Birim / Female");
        await _employeeAnalytics.DidNotReceive().GetActiveHeadcountAsync(_tenantId, Arg.Any<CancellationToken>());
    }
}

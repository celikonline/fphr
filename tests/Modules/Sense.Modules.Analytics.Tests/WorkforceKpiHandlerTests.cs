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
        _employeeAnalytics.GetGenderByDepartmentAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new List<GenderByOrgUnitCount>());
        _orgUnits.ListActiveUnitsAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new List<OrgUnitInfo>());
        // null = Company scope (unrestricted) - the existing tests below all assume the full 5-KPI set; the
        // narrower-scope (unit manager) path is covered separately in GenderRatioByUnit_RestrictedScope_*.
        _dataScope.GetAccessibleOrgUnitIdsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((IReadOnlySet<Guid>?)null);
    }

    private GetWorkforceKpisHandler CreateHandler() => new(_tenant, _employeeAnalytics, _orgUnits, _dataScope, TimeProvider.System);

    /// <summary>G-10-20: 11.3.12'nin tanımı terfi / (terfi + yatay geçiş) - kademe düşürme ve unvan değişikliği
    /// paya girmez, yalnızca kırılımda görünür.</summary>
    [Fact]
    public async Task CareerPathRatio_DividesPromotionsByPromotionsPlusLateralMoves()
    {
        // Counts kept at/above MinCellSize (5) so this test isn't tangled with small-cell suppression (G-10-21,
        // covered separately below) - it only asserts the ratio formula and that breakdown counts pass through.
        _employeeAnalytics.GetActiveHeadcountAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(50);
        _employeeAnalytics.GetCareerPathChangeCountsAsync(_tenantId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new CareerPathChangeCounts(Promotion: 6, LateralMove: 4, Demotion: 3, TitleChange: 8));
        _employeeAnalytics.GetGenderHeadcountAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int>());

        var handler = CreateHandler();
        var result = await handler.Handle(
            new GetWorkforceKpisQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var ratio = result.Value.Single(k => k.Key == KpiKeys.CareerPathRatio);
        ratio.Value.ShouldBe(0.6m); // 6 promotions / (6 promotions + 4 lateral moves) - demotion/title change excluded
        ratio.Breakdown.Single(b => b.Dimension == nameof(CareerPathChangeCounts.Promotion)).Count.ShouldBe(6);
        ratio.Breakdown.Single(b => b.Dimension == nameof(CareerPathChangeCounts.TitleChange)).Count.ShouldBe(8);
    }

    [Fact]
    public async Task CareerPathRatio_IsZero_WhenNoPromotionsOrLateralMoves()
    {
        _employeeAnalytics.GetActiveHeadcountAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(50);
        _employeeAnalytics.GetCareerPathChangeCountsAsync(_tenantId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new CareerPathChangeCounts(Promotion: 0, LateralMove: 0, Demotion: 2, TitleChange: 1));
        _employeeAnalytics.GetGenderHeadcountAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int>());

        var handler = CreateHandler();
        var result = await handler.Handle(
            new GetWorkforceKpisQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)), CancellationToken.None);

        // Demotion/title change only (no promotion/lateral move) -> 0/0, not a divide-by-zero.
        result.Value.Single(k => k.Key == KpiKeys.CareerPathRatio).Value.ShouldBe(0m);
    }

    [Fact]
    public async Task GenderRatio_ReturnsTotalHeadcountAndShares()
    {
        // Both counts kept at/above MinCellSize (5) - see Breakdown_SuppressesCellsUnderMinCellSize for the small-N case.
        _employeeAnalytics.GetActiveHeadcountAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(100);
        _employeeAnalytics.GetCareerPathChangeCountsAsync(_tenantId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new CareerPathChangeCounts(0, 0, 0, 0));
        _employeeAnalytics.GetGenderHeadcountAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int> { ["Female"] = 40, ["Male"] = 60 });

        var handler = CreateHandler();
        var result = await handler.Handle(
            new GetWorkforceKpisQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)), CancellationToken.None);

        var gender = result.Value.Single(k => k.Key == KpiKeys.GenderRatio);
        gender.Value.ShouldBe(100m);
        gender.Breakdown.Single(b => b.Dimension == "Male").Share.ShouldBe(0.6m);
        gender.Breakdown.Single(b => b.Dimension == "Female").Share.ShouldBe(0.4m);
    }

    /// <summary>G-10-21 (11.3.12 "Küçük hücre gizleme"): a non-zero breakdown cell under 5 never leaves the API
    /// with its exact count/share - only that it exists and was suppressed. Applies to every KPI breakdown, checked
    /// here on GENDER_RATIO (company-wide) and GENDER_RATIO_BY_UNIT (the most identity-risky one, per-unit).</summary>
    [Fact]
    public async Task Breakdown_SuppressesCellsUnderMinCellSize_ButNotAtOrAboveIt()
    {
        var smallUnit = Guid.NewGuid();
        var largeUnit = Guid.NewGuid();
        _employeeAnalytics.GetActiveHeadcountAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(9);
        _employeeAnalytics.GetCareerPathChangeCountsAsync(_tenantId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new CareerPathChangeCounts(0, 0, 0, 0));
        _employeeAnalytics.GetGenderHeadcountAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int> { ["Other"] = 2, ["Female"] = 7 }); // Other < 5, Female >= 5
        _employeeAnalytics.GetGenderByOrgUnitAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new List<GenderByOrgUnitCount> { new(smallUnit, "Female", 2), new(largeUnit, "Male", 5) });
        _orgUnits.ListActiveUnitsAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new List<OrgUnitInfo> { new(smallUnit, Guid.NewGuid(), null, "Küçük Birim"), new(largeUnit, Guid.NewGuid(), null, "Büyük Birim") });

        var handler = CreateHandler();
        var result = await handler.Handle(
            new GetWorkforceKpisQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)), CancellationToken.None);

        var genderOther = result.Value.Single(k => k.Key == KpiKeys.GenderRatio).Breakdown.Single(b => b.Dimension == "Other");
        genderOther.IsSuppressed.ShouldBeTrue();
        genderOther.Count.ShouldBeNull();
        genderOther.Share.ShouldBeNull();
        var genderFemale = result.Value.Single(k => k.Key == KpiKeys.GenderRatio).Breakdown.Single(b => b.Dimension == "Female");
        genderFemale.IsSuppressed.ShouldBeFalse();
        genderFemale.Count.ShouldBe(7);

        var byUnit = result.Value.Single(k => k.Key == KpiKeys.GenderRatioByUnit);
        var small = byUnit.Breakdown.Single(b => b.Dimension == "Küçük Birim / Female");
        small.IsSuppressed.ShouldBeTrue();
        small.Count.ShouldBeNull();
        var large = byUnit.Breakdown.Single(b => b.Dimension == "Büyük Birim / Male");
        large.IsSuppressed.ShouldBeFalse(); // exactly at the threshold - not suppressed
        large.Count.ShouldBe(5);
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
        // Counts kept at/above MinCellSize (5, Male exactly at the boundary) - small-cell suppression is
        // covered separately in Breakdown_SuppressesCellsUnderMinCellSize_ButNotAtOrAboveIt.
        var unitId = Guid.NewGuid();
        _employeeAnalytics.GetActiveHeadcountAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(15);
        _employeeAnalytics.GetCareerPathChangeCountsAsync(_tenantId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new CareerPathChangeCounts(0, 0, 0, 0));
        _employeeAnalytics.GetGenderHeadcountAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int>());
        _employeeAnalytics.GetGenderByOrgUnitAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new List<GenderByOrgUnitCount>
            {
                new(unitId, "Female", 10),
                new(null, "Male", 5),
            });
        _orgUnits.ListActiveUnitsAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new List<OrgUnitInfo> { new(unitId, Guid.NewGuid(), null, "Ankara Şubesi") });

        var handler = CreateHandler();
        var result = await handler.Handle(
            new GetWorkforceKpisQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)), CancellationToken.None);

        var byUnit = result.Value.Single(k => k.Key == KpiKeys.GenderRatioByUnit);
        byUnit.Value.ShouldBe(15m);
        byUnit.Breakdown.Single(b => b.Dimension == "Ankara Şubesi / Female").Share.ShouldBe(0.6667m);
        byUnit.Breakdown.Single(b => b.Dimension == "Unassigned / Male").Count.ShouldBe(5);
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
    public async Task RestrictedScope_OnlyReturnsGenderRatioByUnitAndDepartment_FilteredToAccessibleUnits()
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
        _employeeAnalytics.GetGenderByDepartmentAsync(_tenantId, Arg.Any<CancellationToken>())
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

        // Only the two unit/department breakdown KPIs - the four tenant-wide aggregates are excluded rather than
        // shown unscoped.
        result.Value.Count.ShouldBe(2);
        var byUnit = result.Value.Single(k => k.Key == KpiKeys.GenderRatioByUnit);
        byUnit.Value.ShouldBe(2m);
        byUnit.Breakdown.ShouldHaveSingleItem();
        byUnit.Breakdown[0].Dimension.ShouldBe("Erişilebilir Birim / Female");
        var byDepartment = result.Value.Single(k => k.Key == KpiKeys.GenderRatioByDepartment);
        byDepartment.Value.ShouldBe(2m);
        byDepartment.Breakdown.ShouldHaveSingleItem();
        byDepartment.Breakdown[0].Dimension.ShouldBe("Erişilebilir Birim / Female");
        await _employeeAnalytics.DidNotReceive().GetActiveHeadcountAsync(_tenantId, Arg.Any<CancellationToken>());
    }

    /// <summary>senseik-046: GENDER_RATIO_BY_DEPARTMENT is built the same way as GENDER_RATIO_BY_UNIT, just fed the
    /// Department-rolled-up counts (EmployeeAnalyticsQueries.GetGenderByDepartmentAsync does the actual rollup -
    /// covered in Employee.Tests) - here it only needs its own KPI key/label and to read the right source list.</summary>
    [Fact]
    public async Task GenderRatioByDepartment_UsesDepartmentRolledUpCounts_UnderItsOwnKpiKey()
    {
        var departmentUnit = Guid.NewGuid();
        _employeeAnalytics.GetActiveHeadcountAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(20);
        _employeeAnalytics.GetCareerPathChangeCountsAsync(_tenantId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new CareerPathChangeCounts(0, 0, 0, 0));
        _employeeAnalytics.GetGenderHeadcountAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int>());
        _employeeAnalytics.GetGenderByDepartmentAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new List<GenderByOrgUnitCount> { new(departmentUnit, "Female", 10), new(departmentUnit, "Male", 5) });
        _orgUnits.ListActiveUnitsAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new List<OrgUnitInfo> { new(departmentUnit, Guid.NewGuid(), null, "Satış Departmanı") });

        var handler = CreateHandler();
        var result = await handler.Handle(
            new GetWorkforceKpisQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)), CancellationToken.None);

        var byDepartment = result.Value.Single(k => k.Key == KpiKeys.GenderRatioByDepartment);
        byDepartment.Value.ShouldBe(15m);
        byDepartment.Breakdown.Single(b => b.Dimension == "Satış Departmanı / Female").Count.ShouldBe(10);
        byDepartment.Breakdown.Single(b => b.Dimension == "Satış Departmanı / Male").Count.ShouldBe(5);
        // GENDER_RATIO_BY_UNIT stays independent - the handler's default (empty) stub for it must not leak in.
        result.Value.Single(k => k.Key == KpiKeys.GenderRatioByUnit).Breakdown.ShouldBeEmpty();
    }
}

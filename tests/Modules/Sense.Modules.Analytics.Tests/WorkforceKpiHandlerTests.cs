using NSubstitute;
using Sense.Modules.Analytics.Application.Kpis;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Shouldly;
using Xunit;

namespace Sense.Modules.Analytics.Tests;

/// <summary>
/// GetWorkforceKpisHandler: ratio/share math and defaulting, against a fake IEmployeeAnalyticsQueries.
/// No DB - Employee's own EmployeeAnalyticsQueries implementation is covered separately in Employee.Tests.
/// </summary>
public sealed class WorkforceKpiHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly IEmployeeAnalyticsQueries _employeeAnalytics = Substitute.For<IEmployeeAnalyticsQueries>();
    private readonly ITenantContext _tenant;

    public WorkforceKpiHandlerTests()
    {
        _tenant = Substitute.For<ITenantContext>();
        _tenant.TenantId.Returns(_tenantId);
    }

    [Fact]
    public async Task CareerPathRatio_DividesChangesByActiveHeadcount()
    {
        _employeeAnalytics.GetActiveHeadcountAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(50);
        _employeeAnalytics.GetCareerPathChangeCountsAsync(_tenantId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new CareerPathChangeCounts(Promotion: 3, LateralMove: 2, Demotion: 0, TitleChange: 0));
        _employeeAnalytics.GetGenderHeadcountAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int>());

        var handler = new GetWorkforceKpisHandler(_tenant, _employeeAnalytics, TimeProvider.System);
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

        var handler = new GetWorkforceKpisHandler(_tenant, _employeeAnalytics, TimeProvider.System);
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

        var handler = new GetWorkforceKpisHandler(_tenant, _employeeAnalytics, TimeProvider.System);
        var result = await handler.Handle(
            new GetWorkforceKpisQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)), CancellationToken.None);

        var gender = result.Value.Single(k => k.Key == KpiKeys.GenderRatio);
        gender.Value.ShouldBe(10m);
        gender.Breakdown.Single(b => b.Dimension == "Male").Share.ShouldBe(0.6m);
        gender.Breakdown.Single(b => b.Dimension == "Female").Share.ShouldBe(0.4m);
    }

    [Fact]
    public async Task Period_DefaultsToCurrentCalendarYear_WhenNotGiven()
    {
        _employeeAnalytics.GetActiveHeadcountAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(1);
        _employeeAnalytics.GetCareerPathChangeCountsAsync(_tenantId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new CareerPathChangeCounts(0, 0, 0, 0));
        _employeeAnalytics.GetGenderHeadcountAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int>());

        var handler = new GetWorkforceKpisHandler(_tenant, _employeeAnalytics, TimeProvider.System);
        var result = await handler.Handle(new GetWorkforceKpisQuery(), CancellationToken.None);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        result.Value[0].PeriodStart.ShouldBe(new DateOnly(today.Year, 1, 1));
        result.Value[0].PeriodEnd.ShouldBe(today);
    }
}

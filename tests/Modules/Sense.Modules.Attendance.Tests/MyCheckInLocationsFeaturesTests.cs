using NSubstitute;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

/// <summary>Unit tests for "Giriş yapabileceğim lokasyonlar" (FG-005 kabul kriteri 1, Karar 2: çalışan → birim → etkin lokasyon zinciri).</summary>
public sealed class MyCheckInLocationsFeaturesTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _orgUnitId = Guid.NewGuid();
    private readonly Guid _locationId = Guid.NewGuid();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IEmployeeLookup _employees = Substitute.For<IEmployeeLookup>();
    private readonly IOrgUnitLookup _orgUnits = Substitute.For<IOrgUnitLookup>();
    private readonly ILocationLookup _locations = Substitute.For<ILocationLookup>();

    public MyCheckInLocationsFeaturesTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _currentUser.EmployeeId.Returns(_employeeId);
    }

    private GetMyCheckInLocationsHandler CreateHandler() => new(_tenant, _currentUser, _employees, _orgUnits, _locations);

    [Fact]
    public async Task NoLinkedEmployee_ReturnsNotFound()
    {
        _currentUser.EmployeeId.Returns((Guid?)null);

        var result = await CreateHandler().Handle(new GetMyCheckInLocationsQuery(), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceErrors.EmployeeNotFound);
    }

    [Fact]
    public async Task EmployeeWithoutOrgUnit_ReturnsEmptyList()
    {
        _employees.GetOrgUnitIdAsync(_tenantId, _employeeId, Arg.Any<CancellationToken>()).Returns((Guid?)null);

        var result = await CreateHandler().Handle(new GetMyCheckInLocationsQuery(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
        await _orgUnits.DidNotReceive().GetEffectiveLocationIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrgUnitWithoutEffectiveLocation_ReturnsEmptyList()
    {
        _employees.GetOrgUnitIdAsync(_tenantId, _employeeId, Arg.Any<CancellationToken>()).Returns(_orgUnitId);
        _orgUnits.GetEffectiveLocationIdAsync(_tenantId, _orgUnitId, Arg.Any<CancellationToken>()).Returns((Guid?)null);

        var result = await CreateHandler().Handle(new GetMyCheckInLocationsQuery(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
        await _locations.DidNotReceive().GetGeofenceAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EffectiveLocationNotFound_ReturnsEmptyList()
    {
        _employees.GetOrgUnitIdAsync(_tenantId, _employeeId, Arg.Any<CancellationToken>()).Returns(_orgUnitId);
        _orgUnits.GetEffectiveLocationIdAsync(_tenantId, _orgUnitId, Arg.Any<CancellationToken>()).Returns(_locationId);
        _locations.GetGeofenceAsync(_tenantId, _locationId, Arg.Any<CancellationToken>()).Returns((LocationGeofence?)null);

        var result = await CreateHandler().Handle(new GetMyCheckInLocationsQuery(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task LocationWithoutRadius_ReturnsNullRadiusAndCoordinates()
    {
        _employees.GetOrgUnitIdAsync(_tenantId, _employeeId, Arg.Any<CancellationToken>()).Returns(_orgUnitId);
        _orgUnits.GetEffectiveLocationIdAsync(_tenantId, _orgUnitId, Arg.Any<CancellationToken>()).Returns(_locationId);
        _locations.GetGeofenceAsync(_tenantId, _locationId, Arg.Any<CancellationToken>())
            .Returns(new LocationGeofence(_locationId, "Uzak Ofis", null, null, null));

        var result = await CreateHandler().Handle(new GetMyCheckInLocationsQuery(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var location = result.Value.ShouldHaveSingleItem();
        location.RadiusMeters.ShouldBeNull();
        location.Latitude.ShouldBeNull();
        location.Longitude.ShouldBeNull();
    }

    [Fact]
    public async Task LocationWithGeofence_ReturnsSingleLocationWithCoordinatesAndRadius()
    {
        _employees.GetOrgUnitIdAsync(_tenantId, _employeeId, Arg.Any<CancellationToken>()).Returns(_orgUnitId);
        _orgUnits.GetEffectiveLocationIdAsync(_tenantId, _orgUnitId, Arg.Any<CancellationToken>()).Returns(_locationId);
        _locations.GetGeofenceAsync(_tenantId, _locationId, Arg.Any<CancellationToken>())
            .Returns(new LocationGeofence(_locationId, "İstanbul HQ", 39.925533, 32.866287, 150));

        var result = await CreateHandler().Handle(new GetMyCheckInLocationsQuery(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe([new CheckInLocationDto(_locationId, "İstanbul HQ", 39.925533, 32.866287, 150)]);
    }

    /// <summary>The query carries no employee id parameter at all - it can only ever resolve the caller's own chain, so another employee's location can never leak.</summary>
    [Fact]
    public async Task AlwaysResolvesTheCallersOwnEmployeeId_NeverAnotherEmployees()
    {
        var otherEmployeeId = Guid.NewGuid();
        _employees.GetOrgUnitIdAsync(_tenantId, otherEmployeeId, Arg.Any<CancellationToken>()).Returns(_orgUnitId);
        _orgUnits.GetEffectiveLocationIdAsync(_tenantId, _orgUnitId, Arg.Any<CancellationToken>()).Returns(_locationId);
        _locations.GetGeofenceAsync(_tenantId, _locationId, Arg.Any<CancellationToken>())
            .Returns(new LocationGeofence(_locationId, "Başkasının Ofisi", null, null, null));
        _employees.GetOrgUnitIdAsync(_tenantId, _employeeId, Arg.Any<CancellationToken>()).Returns((Guid?)null);

        var result = await CreateHandler().Handle(new GetMyCheckInLocationsQuery(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
        await _employees.Received(1).GetOrgUnitIdAsync(_tenantId, _employeeId, Arg.Any<CancellationToken>());
        await _employees.DidNotReceive().GetOrgUnitIdAsync(_tenantId, otherEmployeeId, Arg.Any<CancellationToken>());
    }
}

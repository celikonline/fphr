using NSubstitute;
using Sense.Modules.Attendance.Application.Availability;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Availability;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

public sealed class EmployeeAvailabilityFeaturesTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IEmployeeAvailabilityRepository _availability = Substitute.For<IEmployeeAvailabilityRepository>();
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();

    public EmployeeAvailabilityFeaturesTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _currentUser.EmployeeId.Returns(_employeeId);
    }

    [Fact]
    public async Task Submit_ValidRequest_AddsAvailability()
    {
        var handler = new SubmitMyAvailabilityHandler(_tenant, _currentUser, _availability);

        var result = await handler.Handle(
            new SubmitMyAvailabilityCommand(new DateOnly(2026, 9, 21), null, null, AvailabilityKind.Unavailable, "not"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        _availability.Received(1).Add(Arg.Is<EmployeeAvailability>(a => a.EmployeeId == _employeeId && a.TenantId == _tenantId));
    }

    [Fact]
    public async Task Submit_NoLinkedEmployee_ReturnsNotFound()
    {
        _currentUser.EmployeeId.Returns((Guid?)null);
        var handler = new SubmitMyAvailabilityHandler(_tenant, _currentUser, _availability);

        var result = await handler.Handle(
            new SubmitMyAvailabilityCommand(new DateOnly(2026, 9, 21), null, null, AvailabilityKind.Unavailable, null), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceErrors.EmployeeNotFound);
    }

    [Fact]
    public async Task Cancel_Owner_Succeeds()
    {
        var availability = EmployeeAvailability.Create(_tenantId, _employeeId, new DateOnly(2026, 9, 21), null, null, AvailabilityKind.Unavailable, null).Value;
        _availability.GetByIdAsync(availability.Id, Arg.Any<CancellationToken>()).Returns(availability);
        var handler = new CancelMyAvailabilityHandler(_currentUser, _availability);

        var result = await handler.Handle(new CancelMyAvailabilityCommand(availability.Id), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        availability.Status.ShouldBe(AvailabilityStatus.Cancelled);
    }

    [Fact]
    public async Task Cancel_NotOwner_ReturnsForbidden()
    {
        var availability = EmployeeAvailability.Create(_tenantId, Guid.NewGuid(), new DateOnly(2026, 9, 21), null, null, AvailabilityKind.Unavailable, null).Value;
        _availability.GetByIdAsync(availability.Id, Arg.Any<CancellationToken>()).Returns(availability);
        var handler = new CancelMyAvailabilityHandler(_currentUser, _availability);

        var result = await handler.Handle(new CancelMyAvailabilityCommand(availability.Id), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceDomainErrors.AvailabilityNotOwner);
    }

    [Fact]
    public async Task ListMine_ReturnsCallersEntries()
    {
        var availability = EmployeeAvailability.Create(_tenantId, _employeeId, new DateOnly(2026, 9, 21), null, null, AvailabilityKind.Unavailable, null).Value;
        _availability.ListForEmployeeAsync(_employeeId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<EmployeeAvailability> { availability });
        var handler = new ListMyAvailabilityHandler(_currentUser, _availability);

        var result = await handler.Handle(new ListMyAvailabilityQuery(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ListForEmployee_OutOfScope_ReturnsForbidden()
    {
        _dataScope.CanAccessEmployeeAsync(AttendancePermissions.AvailabilityView.Key, _employeeId, Arg.Any<CancellationToken>()).Returns(false);
        var handler = new ListEmployeeAvailabilityHandler(_dataScope, _availability);

        var result = await handler.Handle(new ListEmployeeAvailabilityQuery(_employeeId, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceErrors.EmployeeNotInScope);
    }
}

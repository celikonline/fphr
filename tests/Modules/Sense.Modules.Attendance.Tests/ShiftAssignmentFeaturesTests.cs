using NSubstitute;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Application.Shifts;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

public sealed class AssignShiftHandlerTests
{
    // 2026-09-21 is a Monday.
    private static readonly DateOnly Monday = new(2026, 9, 21);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly IShiftAssignmentRepository _shifts = Substitute.For<IShiftAssignmentRepository>();
    private readonly IShiftSettingsRepository _settingsRepository = Substitute.For<IShiftSettingsRepository>();
    private readonly IShiftPlanLockRepository _locks = Substitute.For<IShiftPlanLockRepository>();
    private readonly IEmployeeLookup _employeeLookup = Substitute.For<IEmployeeLookup>();
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly IAttendanceUnitOfWork _unitOfWork = Substitute.For<IAttendanceUnitOfWork>();

    public AssignShiftHandlerTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _dataScope.CanAccessEmployeeAsync(AttendancePermissions.ShiftPlan.Key, _employeeId, Arg.Any<CancellationToken>()).Returns(true);
        _employeeLookup.ExistsActiveAsync(_tenantId, _employeeId, Arg.Any<CancellationToken>()).Returns(true);
        _settingsRepository.FindAsync(Arg.Any<CancellationToken>()).Returns(ShiftSettings.CreateDefault(_tenantId));
        _locks.FindByPeriodAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns((ShiftPlanLock?)null);
        _shifts.ListForRangeAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>())
            .Returns(new List<ShiftAssignment>());
    }

    private AssignShiftHandler CreateHandler() => new(_tenant, _shifts, _settingsRepository, _locks, _employeeLookup, _dataScope, _unitOfWork);

    [Fact]
    public async Task Handle_NewAssignment_Succeeds()
    {
        var result = await CreateHandler().Handle(
            new AssignShiftCommand(_employeeId, Monday, new TimeOnly(9, 0), new TimeOnly(17, 0), 30, false, "Not"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        _shifts.Received(1).Add(Arg.Is<ShiftAssignment>(a => a.EmployeeId == _employeeId && a.Date == Monday));
    }

    [Fact]
    public async Task Handle_EmployeeOutOfScope_ReturnsForbidden()
    {
        _dataScope.CanAccessEmployeeAsync(AttendancePermissions.ShiftPlan.Key, _employeeId, Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateHandler().Handle(
            new AssignShiftCommand(_employeeId, Monday, new TimeOnly(9, 0), new TimeOnly(17, 0), 0, false, null), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceErrors.EmployeeNotInScope);
    }

    [Fact]
    public async Task Handle_LockedPeriod_ReturnsConflict()
    {
        _locks.FindByPeriodAsync(Monday, Arg.Any<CancellationToken>())
            .Returns(ShiftPlanLock.Lock(_tenantId, Monday, DateTime.UtcNow, Guid.NewGuid(), ShiftPlanLockSource.Manual));

        var result = await CreateHandler().Handle(
            new AssignShiftCommand(_employeeId, Monday, new TimeOnly(9, 0), new TimeOnly(17, 0), 0, false, null), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceDomainErrors.ShiftPeriodLocked);
        _shifts.DidNotReceive().Add(Arg.Any<ShiftAssignment>());
    }

    [Fact]
    public async Task Handle_ExceedsDailyLimit_ReturnsValidationError()
    {
        // Varsayılan günlük sınır 11 saat; 14 saatlik bir vardiya aşıyor.
        var result = await CreateHandler().Handle(
            new AssignShiftCommand(_employeeId, Monday, new TimeOnly(6, 0), new TimeOnly(20, 0), 0, false, null), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceDomainErrors.ShiftDailyLimitExceeded);
    }

    [Fact]
    public async Task Handle_ExceedsWeeklyHoursLimit_ReturnsValidationError()
    {
        // Varsayılan haftalık sınır 45 saat; haftada zaten 40 saat planlanmışken 8 saat daha eklemek aşıyor.
        var existing = Enumerable.Range(0, 5)
            .Select(i => ShiftAssignment.Create(_tenantId, _employeeId, Monday.AddDays(i), new TimeOnly(9, 0), new TimeOnly(17, 0), 0, false, null).Value)
            .ToList();
        _shifts.ListForRangeAsync(Monday, Monday.AddDays(6), Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>()).Returns(existing);

        var result = await CreateHandler().Handle(
            new AssignShiftCommand(_employeeId, Monday.AddDays(5), new TimeOnly(9, 0), new TimeOnly(17, 0), 0, false, null), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceDomainErrors.ShiftWeeklyHoursExceeded);
    }

    [Fact]
    public async Task Handle_ExceedsWeeklyDaysLimit_ReturnsValidationError()
    {
        // Varsayılan haftalık gün sınırı 6; 6 gün zaten planlıyken 7. günü eklemek aşıyor.
        var existing = Enumerable.Range(0, 6)
            .Select(i => ShiftAssignment.Create(_tenantId, _employeeId, Monday.AddDays(i), new TimeOnly(9, 0), new TimeOnly(13, 0), 0, false, null).Value)
            .ToList();
        _shifts.ListForRangeAsync(Monday, Monday.AddDays(6), Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>()).Returns(existing);

        var result = await CreateHandler().Handle(
            new AssignShiftCommand(_employeeId, Monday.AddDays(6), new TimeOnly(9, 0), new TimeOnly(13, 0), 0, false, null), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceDomainErrors.ShiftWeeklyDaysExceeded);
    }

    [Fact]
    public async Task Handle_ExistingAssignmentSameDay_UpdatesInsteadOfAdding()
    {
        var existing = ShiftAssignment.Create(_tenantId, _employeeId, Monday, new TimeOnly(9, 0), new TimeOnly(17, 0), 0, false, null).Value;
        _shifts.FindByEmployeeAndDateAsync(_employeeId, Monday, Arg.Any<CancellationToken>()).Returns(existing);
        _shifts.ListForRangeAsync(Monday, Monday.AddDays(6), Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>()).Returns([existing]);

        var result = await CreateHandler().Handle(
            new AssignShiftCommand(_employeeId, Monday, new TimeOnly(10, 0), new TimeOnly(18, 0), 0, false, "Güncellendi"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        existing.Start.ShouldBe(new TimeOnly(10, 0));
        existing.Note.ShouldBe("Güncellendi");
        _shifts.DidNotReceive().Add(Arg.Any<ShiftAssignment>());
    }
}

public sealed class PublishShiftAssignmentsHandlerTests
{
    private readonly IShiftAssignmentRepository _shifts = Substitute.For<IShiftAssignmentRepository>();
    private readonly IShiftPlanLockRepository _locks = Substitute.For<IShiftPlanLockRepository>();

    public PublishShiftAssignmentsHandlerTests() =>
        _locks.FindByPeriodAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns((ShiftPlanLock?)null);

    [Fact]
    public async Task Handle_DraftAssignment_Publishes()
    {
        var assignment = ShiftAssignment.Create(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 21), new TimeOnly(9, 0), new TimeOnly(17, 0), 0, false, null).Value;
        _shifts.GetByIdAsync(assignment.Id, Arg.Any<CancellationToken>()).Returns(assignment);
        var handler = new PublishShiftAssignmentsHandler(_shifts, _locks, TimeProvider.System);

        var result = await handler.Handle(new PublishShiftAssignmentsCommand([assignment.Id]), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        assignment.Status.ShouldBe(ShiftAssignmentStatus.Published);
    }

    [Fact]
    public async Task Handle_LockedPeriod_Fails()
    {
        var date = new DateOnly(2026, 9, 21);
        var assignment = ShiftAssignment.Create(Guid.NewGuid(), Guid.NewGuid(), date, new TimeOnly(9, 0), new TimeOnly(17, 0), 0, false, null).Value;
        _shifts.GetByIdAsync(assignment.Id, Arg.Any<CancellationToken>()).Returns(assignment);
        _locks.FindByPeriodAsync(date, Arg.Any<CancellationToken>())
            .Returns(ShiftPlanLock.Lock(Guid.NewGuid(), date, DateTime.UtcNow, Guid.NewGuid(), ShiftPlanLockSource.Manual));
        var handler = new PublishShiftAssignmentsHandler(_shifts, _locks, TimeProvider.System);

        var result = await handler.Handle(new PublishShiftAssignmentsCommand([assignment.Id]), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceDomainErrors.ShiftPeriodLocked);
        assignment.Status.ShouldBe(ShiftAssignmentStatus.Draft);
    }
}

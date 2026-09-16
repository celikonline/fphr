using NSubstitute;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Application.Compensatory;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Contracts.Events;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Compensatory;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

public sealed class CompensatoryWorkFeaturesTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private static readonly DateOnly WeekStart = new(2026, 9, 21);

    private readonly IAttendanceDayRepository _days = Substitute.For<IAttendanceDayRepository>();
    private readonly ICompensatoryWorkLedgerRepository _ledger = Substitute.For<ICompensatoryWorkLedgerRepository>();
    private readonly IAttendanceUnitOfWork _unitOfWork = Substitute.For<IAttendanceUnitOfWork>();
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();

    private TimesheetWeekCompletedHandler CreateHandler() => new(_days, _ledger, _unitOfWork);

    private AttendanceDay ShortWeekDay(int plannedMinutes) =>
        AttendanceDay.Record(_tenantId, _employeeId, WeekStart, null, null, new TimeOnly(9, 0), new TimeOnly(9, 0).Add(TimeSpan.FromMinutes(plannedMinutes)), plannedMinutes, 0, AttendanceDaySource.Manual);

    [Fact]
    public async Task Handle_WeekBelowStandardMinutes_OpensNewLedger()
    {
        // Tek gün, 4 saat planlanmış: 45s/hafta normunun 2340 dakika altında bir açık = telafi borcu.
        _days.ListForEmployeeAsync(_employeeId, WeekStart, WeekStart.AddDays(6), Arg.Any<CancellationToken>())
            .Returns(new List<AttendanceDay> { ShortWeekDay(4 * 60) });
        _ledger.FindByEmployeeAndWeekAsync(_employeeId, WeekStart, Arg.Any<CancellationToken>()).Returns((CompensatoryWorkLedger?)null);

        await CreateHandler().Handle(new TimesheetWeekCompleted(_tenantId, _employeeId, WeekStart), TestContext.Current.CancellationToken);

        _ledger.Received(1).Add(Arg.Is<CompensatoryWorkLedger>(l =>
            l.EmployeeId == _employeeId && l.DeficitMinutes == CompensatoryWorkLimits.StandardWeeklyMinutes - 4 * 60));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WeekAtOrAboveStandardMinutes_DoesNothing()
    {
        _days.ListForEmployeeAsync(_employeeId, WeekStart, WeekStart.AddDays(6), Arg.Any<CancellationToken>())
            .Returns(new List<AttendanceDay> { ShortWeekDay(CompensatoryWorkLimits.StandardWeeklyMinutes) });
        _ledger.FindByEmployeeAndWeekAsync(_employeeId, WeekStart, Arg.Any<CancellationToken>()).Returns((CompensatoryWorkLedger?)null);

        await CreateHandler().Handle(new TimesheetWeekCompleted(_tenantId, _employeeId, WeekStart), TestContext.Current.CancellationToken);

        _ledger.DidNotReceive().Add(Arg.Any<CompensatoryWorkLedger>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExistingOpenLedgerWithChangedDeficit_Adjusts()
    {
        var existing = CompensatoryWorkLedger.Open(_tenantId, _employeeId, WeekStart, 100, WeekStart.AddMonths(CompensatoryWorkLimits.WindowMonths));
        _days.ListForEmployeeAsync(_employeeId, WeekStart, WeekStart.AddDays(6), Arg.Any<CancellationToken>())
            .Returns(new List<AttendanceDay> { ShortWeekDay(4 * 60) });
        _ledger.FindByEmployeeAndWeekAsync(_employeeId, WeekStart, Arg.Any<CancellationToken>()).Returns(existing);

        await CreateHandler().Handle(new TimesheetWeekCompleted(_tenantId, _employeeId, WeekStart), TestContext.Current.CancellationToken);

        existing.DeficitMinutes.ShouldBe(CompensatoryWorkLimits.StandardWeeklyMinutes - 4 * 60);
        _ledger.DidNotReceive().Add(Arg.Any<CompensatoryWorkLedger>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExistingLedgerSameDeficit_NoOp()
    {
        var deficit = CompensatoryWorkLimits.StandardWeeklyMinutes - 4 * 60;
        var existing = CompensatoryWorkLedger.Open(_tenantId, _employeeId, WeekStart, deficit, WeekStart.AddMonths(CompensatoryWorkLimits.WindowMonths));
        _days.ListForEmployeeAsync(_employeeId, WeekStart, WeekStart.AddDays(6), Arg.Any<CancellationToken>())
            .Returns(new List<AttendanceDay> { ShortWeekDay(4 * 60) });
        _ledger.FindByEmployeeAndWeekAsync(_employeeId, WeekStart, Arg.Any<CancellationToken>()).Returns(existing);

        await CreateHandler().Handle(new TimesheetWeekCompleted(_tenantId, _employeeId, WeekStart), TestContext.Current.CancellationToken);

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordCompensation_ValidLedger_RecordsMinutes()
    {
        var ledger = CompensatoryWorkLedger.Open(_tenantId, _employeeId, WeekStart, 120, WeekStart.AddMonths(CompensatoryWorkLimits.WindowMonths));
        _ledger.GetByIdAsync(ledger.Id, Arg.Any<CancellationToken>()).Returns(ledger);
        var handler = new RecordCompensationHandler(_ledger);

        var result = await handler.Handle(new RecordCompensationCommand(ledger.Id, 60), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        ledger.CompensatedMinutes.ShouldBe(60);
    }

    [Fact]
    public async Task ListCompensatoryLedger_OutOfScope_ReturnsForbidden()
    {
        _dataScope.CanAccessEmployeeAsync(AttendancePermissions.CompensatoryViewKey, _employeeId, Arg.Any<CancellationToken>()).Returns(false);
        var handler = new ListCompensatoryLedgerHandler(_dataScope, _ledger);

        var result = await handler.Handle(new ListCompensatoryLedgerQuery(_employeeId, WeekStart, WeekStart), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceErrors.EmployeeNotInScope);
    }
}

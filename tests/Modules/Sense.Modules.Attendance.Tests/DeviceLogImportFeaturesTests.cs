using NSubstitute;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Application.DeviceLogs;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

public sealed class ImportDeviceLogsHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly IEmployeeLookup _employeeLookup = Substitute.For<IEmployeeLookup>();
    private readonly IWorkScheduleReadService _schedules = Substitute.For<IWorkScheduleReadService>();
    private readonly IAttendanceDayRepository _days = Substitute.For<IAttendanceDayRepository>();
    private readonly IAttendanceUnitOfWork _unitOfWork = Substitute.For<IAttendanceUnitOfWork>();

    private const string EmployeeCode = "10000000146";
    private static readonly DateOnly Date = new(2026, 9, 16);

    public ImportDeviceLogsHandlerTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _schedules.GetForDateAsync(_employeeId, Date, Arg.Any<CancellationToken>())
            .Returns(new EmployeeScheduleDay(true, new TimeOnly(9, 0), new TimeOnly(18, 0), null, null, true));
    }

    private ImportDeviceLogsHandler CreateHandler() => new(_tenant, _employeeLookup, _schedules, _days, _unitOfWork);

    [Fact]
    public async Task Handle_InAndOutPunchesSameDay_UpsertsSingleAttendanceDay()
    {
        _employeeLookup.FindActiveByNationalIdAsync(_tenantId, EmployeeCode, Arg.Any<CancellationToken>()).Returns(_employeeId);
        _days.FindByEmployeeAndDateAsync(_employeeId, Date, Arg.Any<CancellationToken>()).Returns((AttendanceDay?)null);
        const string csv = "employeeCode;timestamp;direction\n" +
                            "10000000146;2026-09-16T08:55:00;in\n" +
                            "10000000146;2026-09-16T18:05:00;out\n";

        var result = await CreateHandler().Handle(new ImportDeviceLogsCommand(csv), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Imported.ShouldBe(2);
        result.Value.Skipped.ShouldBe(0);
        result.Value.Errors.ShouldBeEmpty();
        _days.Received(1).Add(Arg.Is<AttendanceDay>(d => d.CheckIn == new TimeOnly(8, 55) && d.CheckOut == new TimeOnly(18, 5)));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MultipleInPunches_UsesEarliestAsCheckIn()
    {
        _employeeLookup.FindActiveByNationalIdAsync(_tenantId, EmployeeCode, Arg.Any<CancellationToken>()).Returns(_employeeId);
        _days.FindByEmployeeAndDateAsync(_employeeId, Date, Arg.Any<CancellationToken>()).Returns((AttendanceDay?)null);
        const string csv = "employeeCode;timestamp;direction\n" +
                            "10000000146;2026-09-16T09:10:00;in\n" +
                            "10000000146;2026-09-16T08:55:00;in\n" +
                            "10000000146;2026-09-16T18:05:00;out\n";

        var result = await CreateHandler().Handle(new ImportDeviceLogsCommand(csv), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        _days.Received(1).Add(Arg.Is<AttendanceDay>(d => d.CheckIn == new TimeOnly(8, 55)));
    }

    [Fact]
    public async Task Handle_UnknownEmployeeCode_SkipsRowWithError()
    {
        _employeeLookup.FindActiveByNationalIdAsync(_tenantId, EmployeeCode, Arg.Any<CancellationToken>()).Returns((Guid?)null);
        const string csv = "employeeCode;timestamp;direction\n10000000146;2026-09-16T08:55:00;in\n";

        var result = await CreateHandler().Handle(new ImportDeviceLogsCommand(csv), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Imported.ShouldBe(0);
        result.Value.Skipped.ShouldBe(1);
        result.Value.Errors.ShouldContain(e => e.Line == 2 && e.Code == DeviceLogErrorCodes.UnknownEmployee);
        _days.DidNotReceive().Add(Arg.Any<AttendanceDay>());
    }

    [Fact]
    public async Task Handle_DuplicateEmployeeAndTimestamp_SkipsSecondOccurrence()
    {
        _employeeLookup.FindActiveByNationalIdAsync(_tenantId, EmployeeCode, Arg.Any<CancellationToken>()).Returns(_employeeId);
        _days.FindByEmployeeAndDateAsync(_employeeId, Date, Arg.Any<CancellationToken>()).Returns((AttendanceDay?)null);
        const string csv = "employeeCode;timestamp;direction\n" +
                            "10000000146;2026-09-16T08:55:00;in\n" +
                            "10000000146;2026-09-16T08:55:00;in\n";

        var result = await CreateHandler().Handle(new ImportDeviceLogsCommand(csv), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Imported.ShouldBe(1);
        result.Value.Skipped.ShouldBe(1);
        result.Value.Errors.ShouldContain(e => e.Line == 3 && e.Code == DeviceLogErrorCodes.DuplicateTimestamp);
    }

    [Fact]
    public async Task Handle_DerivedCheckOutBeforeCheckIn_SkipsGroupWithError()
    {
        _employeeLookup.FindActiveByNationalIdAsync(_tenantId, EmployeeCode, Arg.Any<CancellationToken>()).Returns(_employeeId);
        // "out" at 07:00 is before the single "in" at 09:00 on the same day - malformed device data.
        const string csv = "employeeCode;timestamp;direction\n" +
                            "10000000146;2026-09-16T09:00:00;in\n" +
                            "10000000146;2026-09-16T07:00:00;out\n";

        var result = await CreateHandler().Handle(new ImportDeviceLogsCommand(csv), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Imported.ShouldBe(0);
        result.Value.Skipped.ShouldBe(2);
        result.Value.Errors.ShouldAllBe(e => e.Code == DeviceLogErrorCodes.CheckOutBeforeCheckIn);
        _days.DidNotReceive().Add(Arg.Any<AttendanceDay>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExistingAttendanceDay_UpdatesInsteadOfAdding()
    {
        var existing = AttendanceDay.Record(_tenantId, _employeeId, Date, null, null, new TimeOnly(9, 0), new TimeOnly(18, 0), 540, 0, AttendanceDaySource.Manual);
        _employeeLookup.FindActiveByNationalIdAsync(_tenantId, EmployeeCode, Arg.Any<CancellationToken>()).Returns(_employeeId);
        _days.FindByEmployeeAndDateAsync(_employeeId, Date, Arg.Any<CancellationToken>()).Returns(existing);
        const string csv = "employeeCode;timestamp;direction\n10000000146;2026-09-16T08:55:00;in\n";

        var result = await CreateHandler().Handle(new ImportDeviceLogsCommand(csv), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        existing.CheckIn.ShouldBe(new TimeOnly(8, 55));
        existing.Source.ShouldBe(AttendanceDaySource.Device);
        _days.DidNotReceive().Add(Arg.Any<AttendanceDay>());
    }

    [Fact]
    public async Task Handle_ParserLevelError_IsIncludedInResultErrors()
    {
        const string csv = "employeeCode;timestamp;direction\n;2026-09-16T08:55:00;in\n";

        var result = await CreateHandler().Handle(new ImportDeviceLogsCommand(csv), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Skipped.ShouldBe(1);
        result.Value.Errors.ShouldContain(e => e.Code == DeviceLogErrorCodes.MissingEmployeeCode);
    }
}

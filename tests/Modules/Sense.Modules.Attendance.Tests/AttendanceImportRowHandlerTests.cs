using NSubstitute;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Application.Import;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Import;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

public sealed class AttendanceImportRowHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly IEmployeeLookup _employeeLookup = Substitute.For<IEmployeeLookup>();
    private readonly IWorkScheduleReadService _schedules = Substitute.For<IWorkScheduleReadService>();
    private readonly IAttendanceDayRepository _days = Substitute.For<IAttendanceDayRepository>();
    private readonly IAttendanceUnitOfWork _unitOfWork = Substitute.For<IAttendanceUnitOfWork>();

    private const string NationalId = "10000000146";
    private static readonly DateOnly Date = new(2026, 9, 21);

    public AttendanceImportRowHandlerTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _schedules.GetForDateAsync(_employeeId, Date, Arg.Any<CancellationToken>())
            .Returns(new EmployeeScheduleDay(true, new TimeOnly(9, 0), new TimeOnly(18, 0), null, null, true));
    }

    private AttendanceImportRowHandler CreateHandler() =>
        new(_tenant, _employeeLookup, _schedules, _days, _unitOfWork, new InMemoryPunchRepository(), new FixedLocalClock(DateTime.UtcNow));

    private static ImportRow Row(int rowNumber, string? nationalId, string? date, string? checkIn, string? checkOut)
    {
        var values = new Dictionary<string, ImportCell>();
        if (nationalId is not null)
        {
            values[AttendanceImportColumns.NationalId] = new ImportCell(nationalId);
        }

        if (date is not null)
        {
            values[AttendanceImportColumns.Date] = new ImportCell(date);
        }

        if (checkIn is not null)
        {
            values[AttendanceImportColumns.CheckIn] = new ImportCell(checkIn);
        }

        if (checkOut is not null)
        {
            values[AttendanceImportColumns.CheckOut] = new ImportCell(checkOut);
        }

        return new ImportRow(rowNumber, values);
    }

    [Fact]
    public async Task ProcessAsync_ValidRow_InsertsNewAttendanceDay()
    {
        _employeeLookup.FindActiveByNationalIdAsync(_tenantId, NationalId, Arg.Any<CancellationToken>()).Returns(_employeeId);
        _days.FindByEmployeeAndDateAsync(_employeeId, Date, Arg.Any<CancellationToken>()).Returns((AttendanceDay?)null);

        var batch = new ImportBatch(ImportMode.Upsert, ImportFailurePolicy.SkipInvalidRows, [AttendanceImportColumns.NationalId], [Row(2, NationalId, "2026-09-21", "09:00", "18:00")]);
        var result = await CreateHandler().ProcessAsync(batch, TestContext.Current.CancellationToken);

        result.Applied.ShouldBeTrue();
        result.Rows.ShouldHaveSingleItem();
        result.Rows[0].Outcome.ShouldBe(ImportRowOutcome.Inserted);
        _days.Received(1).Add(Arg.Any<AttendanceDay>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_ExistingDay_UpdatesInsteadOfInserting()
    {
        var existing = AttendanceDay.Record(_tenantId, _employeeId, Date, null, null, new TimeOnly(9, 0), new TimeOnly(18, 0), 540, 0, AttendanceDaySource.Manual);
        _employeeLookup.FindActiveByNationalIdAsync(_tenantId, NationalId, Arg.Any<CancellationToken>()).Returns(_employeeId);
        _days.FindByEmployeeAndDateAsync(_employeeId, Date, Arg.Any<CancellationToken>()).Returns(existing);

        var batch = new ImportBatch(ImportMode.Upsert, ImportFailurePolicy.SkipInvalidRows, [AttendanceImportColumns.NationalId], [Row(2, NationalId, "2026-09-21", "09:00", "18:00")]);
        var result = await CreateHandler().ProcessAsync(batch, TestContext.Current.CancellationToken);

        result.Rows[0].Outcome.ShouldBe(ImportRowOutcome.Updated);
        existing.Source.ShouldBe(AttendanceDaySource.Import);
        existing.Anomalies.ShouldBe(AttendanceAnomaly.None);
        _days.DidNotReceive().Add(Arg.Any<AttendanceDay>());
    }

    [Fact]
    public async Task ProcessAsync_UnmatchedEmployee_ReportsNoMatch()
    {
        _employeeLookup.FindActiveByNationalIdAsync(_tenantId, NationalId, Arg.Any<CancellationToken>()).Returns((Guid?)null);

        var batch = new ImportBatch(ImportMode.Upsert, ImportFailurePolicy.SkipInvalidRows, [AttendanceImportColumns.NationalId], [Row(2, NationalId, "2026-09-21", "09:00", "18:00")]);
        var result = await CreateHandler().ProcessAsync(batch, TestContext.Current.CancellationToken);

        result.Rows[0].Outcome.ShouldBe(ImportRowOutcome.Error);
        result.Rows[0].Errors.ShouldContain(e => e.Code == ImportErrorCodes.NoMatch);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_CheckOutBeforeCheckIn_ReportsError()
    {
        _employeeLookup.FindActiveByNationalIdAsync(_tenantId, NationalId, Arg.Any<CancellationToken>()).Returns(_employeeId);

        var batch = new ImportBatch(ImportMode.Upsert, ImportFailurePolicy.SkipInvalidRows, [AttendanceImportColumns.NationalId], [Row(2, NationalId, "2026-09-21", "18:00", "09:00")]);
        var result = await CreateHandler().ProcessAsync(batch, TestContext.Current.CancellationToken);

        result.Rows[0].Outcome.ShouldBe(ImportRowOutcome.Error);
        result.Rows[0].Errors.ShouldContain(e => e.Code == AttendanceImportErrorCodes.CheckOutBeforeCheckIn);
    }

    [Fact]
    public async Task ProcessAsync_MissingNationalIdOrDate_ReportsRequiredMissing()
    {
        var batch = new ImportBatch(ImportMode.Upsert, ImportFailurePolicy.SkipInvalidRows, [], [Row(2, null, null, "09:00", "18:00")]);
        var result = await CreateHandler().ProcessAsync(batch, TestContext.Current.CancellationToken);

        result.Rows[0].Outcome.ShouldBe(ImportRowOutcome.Error);
        result.Rows[0].Errors.Count(e => e.Code == ImportErrorCodes.RequiredMissing).ShouldBe(2);
    }

    [Fact]
    public async Task ProcessAsync_AllOrNothing_SkipsValidRowsWhenAnyRowFails()
    {
        _employeeLookup.FindActiveByNationalIdAsync(_tenantId, NationalId, Arg.Any<CancellationToken>()).Returns(_employeeId);
        _days.FindByEmployeeAndDateAsync(_employeeId, Date, Arg.Any<CancellationToken>()).Returns((AttendanceDay?)null);

        var rows = new[]
        {
            Row(2, NationalId, "2026-09-21", "09:00", "18:00"),
            Row(3, "00000000000", "2026-09-21", "09:00", "18:00"),
        };
        _employeeLookup.FindActiveByNationalIdAsync(_tenantId, "00000000000", Arg.Any<CancellationToken>()).Returns((Guid?)null);

        var batch = new ImportBatch(ImportMode.Upsert, ImportFailurePolicy.AllOrNothing, [AttendanceImportColumns.NationalId], rows);
        var result = await CreateHandler().ProcessAsync(batch, TestContext.Current.CancellationToken);

        result.Applied.ShouldBeFalse();
        result.Rows.First(r => r.RowNumber == 2).Outcome.ShouldBe(ImportRowOutcome.Skipped);
        result.Rows.First(r => r.RowNumber == 3).Outcome.ShouldBe(ImportRowOutcome.Error);
        _days.DidNotReceive().Add(Arg.Any<AttendanceDay>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}

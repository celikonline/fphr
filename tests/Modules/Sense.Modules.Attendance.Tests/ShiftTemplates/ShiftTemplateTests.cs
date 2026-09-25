using NSubstitute;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Application.Import;
using Sense.Modules.Attendance.Application.ShiftTemplates;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Availability;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Import;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.ShiftTemplates;

/// <summary>Z3.2: plan şablonu kaydet/uygula ve "shift-plan" Excel içe aktarma.</summary>
public sealed class ShiftTemplateTests
{
    // 2026-09-21 Monday; 2026-10-05 Monday (next period), October is not locked unless a test says so.
    private static readonly DateOnly SourceStart = new(2026, 9, 21);
    private static readonly DateOnly TargetStart = new(2026, 10, 5);
    private const string NationalId = "10000000146";

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IShiftPlanTemplateRepository _templates = Substitute.For<IShiftPlanTemplateRepository>();
    private readonly IShiftDefinitionRepository _definitions = Substitute.For<IShiftDefinitionRepository>();
    private readonly IShiftAssignmentRepository _shifts = Substitute.For<IShiftAssignmentRepository>();
    private readonly IShiftSettingsRepository _settings = Substitute.For<IShiftSettingsRepository>();
    private readonly IShiftPlanLockRepository _locks = Substitute.For<IShiftPlanLockRepository>();
    private readonly IEmployeeAvailabilityRepository _availability = Substitute.For<IEmployeeAvailabilityRepository>();
    private readonly IEmployeeLookup _employeeLookup = Substitute.For<IEmployeeLookup>();
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();
    private readonly IAttendanceUnitOfWork _unitOfWork = Substitute.For<IAttendanceUnitOfWork>();
    private readonly ShiftDefinition _morning;
    private readonly ShiftDefinition _night;
    private readonly List<ShiftAssignment> _existing = [];

    public ShiftTemplateTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _currentUser.UserId.Returns(_userId);
        _morning = Definition("SAB", 9 * 60, 17 * 60);
        _night = Definition("GEC", 22 * 60, 6 * 60);
        _definitions.ListByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(ci => new List<ShiftDefinition> { _morning, _night }.Where(d => ci.Arg<IReadOnlyCollection<Guid>>().Contains(d.Id)).ToList());
        _definitions.ListAsync(false, Arg.Any<CancellationToken>()).Returns(new List<ShiftDefinition> { _night, _morning });
        _dataScope.CanAccessEmployeeAsync(AttendancePermissions.ShiftPlan.Key, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        _employeeLookup.ExistsActiveAsync(_tenantId, _employeeId, Arg.Any<CancellationToken>()).Returns(true);
        _employeeLookup.FindActiveByNationalIdAsync(_tenantId, NationalId, Arg.Any<CancellationToken>()).Returns(_employeeId);
        _settings.FindAsync(Arg.Any<CancellationToken>()).Returns(ShiftSettings.CreateDefault(_tenantId));
        _locks.FindByPeriodAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns((ShiftPlanLock?)null);
        _shifts.ListForRangeAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>())
            .Returns(ci => _existing.Where(a => a.Date >= ci.ArgAt<DateOnly>(0) && a.Date <= ci.ArgAt<DateOnly>(1)).ToList());
        _availability.ListForEmployeeAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<EmployeeAvailability>());
    }

    private ShiftDefinition Definition(string code, int start, int end) => ShiftDefinition.Create(
        _tenantId,
        new ShiftDefinitionValues(
            code, code, "#112233", ShiftType.Fixed, start, end, StartsPreviousDay: false, null, null, null, [ShiftBreak.Fixed(12 * 60, 30)],
            0, 0, null, null, null, null, 0)).Value;

    private CreateShiftTemplateHandler CreateHandler() => new(_tenant, _currentUser, _templates, _definitions, _employeeLookup, _dataScope);

    private ApplyShiftTemplateHandler ApplyHandler() =>
        new(_tenant, _templates, _definitions, _shifts, _settings, _locks, _availability, _employeeLookup, _dataScope, _unitOfWork);

    private ShiftPlanImportRowHandler ImportHandler() =>
        new(_tenant, _employeeLookup, _definitions, _shifts, _settings, _locks, _availability, _unitOfWork);

    private static ImportRow Row(int number, string? nationalId, string? date, string? code)
    {
        var values = new Dictionary<string, ImportCell>();
        if (nationalId is not null)
        {
            values[ShiftPlanImportColumns.NationalId] = new ImportCell(nationalId);
        }

        if (date is not null)
        {
            values[ShiftPlanImportColumns.Date] = new ImportCell(date);
        }

        if (code is not null)
        {
            values[ShiftPlanImportColumns.ShiftCode] = new ImportCell(code);
        }

        return new ImportRow(number, values);
    }

    private static ImportBatch Batch(ImportFailurePolicy policy, params ImportRow[] rows) =>
        new(ImportMode.Upsert, policy, [ShiftPlanImportColumns.NationalId], rows);

    private async Task<ShiftPlanTemplate> SavedTemplate()
    {
        ShiftPlanTemplate? saved = null;
        _templates.When(t => t.Add(Arg.Any<ShiftPlanTemplate>())).Do(ci => saved = ci.Arg<ShiftPlanTemplate>());
        var created = await CreateHandler().Handle(
            new CreateShiftTemplateCommand(
                "Hafta", 7,
                [
                    new ShiftTemplateEntryDto(_employeeId, 0, _morning.Id, ShiftDayType.Work),
                    new ShiftTemplateEntryDto(_employeeId, 1, _night.Id, ShiftDayType.Work),
                    new ShiftTemplateEntryDto(_employeeId, 6, _morning.Id, ShiftDayType.WeeklyRest),
                ]),
            TestContext.Current.CancellationToken);
        created.IsSuccess.ShouldBeTrue();
        _templates.GetByIdAsync(saved!.Id, Arg.Any<CancellationToken>()).Returns(saved);
        return saved;
    }

    [Fact]
    public async Task Template_roundtrip_apply_reproduces_entries()
    {
        var template = await SavedTemplate();
        template.CreatedByUserId.ShouldBe(_userId);
        template.Entries.Single(e => e.DayOffset == 6).ShiftDefinitionId.ShouldBeNull("rest cell keeps no definition");
        var added = new List<ShiftAssignment>();
        _shifts.When(s => s.Add(Arg.Any<ShiftAssignment>())).Do(ci => added.Add(ci.Arg<ShiftAssignment>()));

        var result = await ApplyHandler().Handle(new ApplyShiftTemplateCommand(template.Id, TargetStart, false), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AffectedCount.ShouldBe(3);
        result.Value.Skipped.ShouldBeEmpty();
        added.Select(a => (a.EmployeeId, a.Date, a.ShiftDefinitionId, a.DayType)).OrderBy(x => x.Date).ToList().ShouldBe(
        [
            (_employeeId, TargetStart, (Guid?)_morning.Id, ShiftDayType.Work),
            (_employeeId, TargetStart.AddDays(1), (Guid?)_night.Id, ShiftDayType.Work),
            (_employeeId, TargetStart.AddDays(6), (Guid?)null, ShiftDayType.WeeklyRest),
        ]);
        added[0].Start.ShouldBe(new TimeOnly(9, 0));
        added[0].End.ShouldBe(new TimeOnly(17, 0));
        added[0].BreakMinutes.ShouldBe(30);
    }

    [Fact]
    public async Task Apply_skips_locked_month_and_published_cells_unless_overwrite()
    {
        var template = await SavedTemplate();
        // Template day 1 lands on 2026-10-06 (published existing), day 6 lands on 2026-10-11; lock October entirely only for day 6 via a lock on the month.
        var published = ShiftAssignment.Create(_tenantId, _employeeId, TargetStart.AddDays(1), new TimeOnly(9, 0), new TimeOnly(17, 0), 30, false, null).Value;
        published.Publish(DateTime.UtcNow);
        _existing.Add(published);

        var skippedPublished = await ApplyHandler().Handle(new ApplyShiftTemplateCommand(template.Id, TargetStart, false), TestContext.Current.CancellationToken);
        skippedPublished.Value.Skipped.ShouldHaveSingleItem().Reason.ShouldBe(ShiftTemplateSkipReason.Published);
        skippedPublished.Value.AffectedCount.ShouldBe(2);

        var overwrite = await ApplyHandler().Handle(new ApplyShiftTemplateCommand(template.Id, TargetStart, true), TestContext.Current.CancellationToken);
        overwrite.Value.Skipped.ShouldBeEmpty();

        var lockedMonth = ShiftPlanLock.Lock(_tenantId, new DateOnly(2026, 10, 1), DateTime.UtcNow, _userId, ShiftPlanLockSource.Manual);
        _locks.FindByPeriodAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(lockedMonth);
        var locked = await ApplyHandler().Handle(new ApplyShiftTemplateCommand(template.Id, TargetStart, true), TestContext.Current.CancellationToken);
        locked.Value.AffectedCount.ShouldBe(0);
        locked.Value.Skipped.Count.ShouldBe(3);
        locked.Value.Skipped.ShouldAllBe(s => s.Reason == ShiftTemplateSkipReason.Locked);
    }

    [Fact]
    public async Task Import_row_with_unknown_shift_code_fails_row_only()
    {
        var batch = Batch(
            ImportFailurePolicy.SkipInvalidRows,
            Row(2, NationalId, "2026-10-05", "SAB"),
            Row(3, NationalId, "2026-10-06", "YOK"),
            Row(4, NationalId, "2026-10-07", "weekly_rest"));

        var result = await ImportHandler().ProcessAsync(batch, TestContext.Current.CancellationToken);

        result.Applied.ShouldBeTrue();
        result.Rows.Select(r => r.Outcome).ShouldBe([ImportRowOutcome.Inserted, ImportRowOutcome.Error, ImportRowOutcome.Inserted]);
        result.Rows[1].Errors.ShouldHaveSingleItem().Code.ShouldBe(ImportErrorCodes.LookupNotFound);
        _shifts.Received(2).Add(Arg.Any<ShiftAssignment>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Import_respects_locked_period()
    {
        _locks.FindByPeriodAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(
            ci => ci.Arg<DateOnly>().Month == 9 ? ShiftPlanLock.Lock(_tenantId, new DateOnly(2026, 9, 1), DateTime.UtcNow, _userId, ShiftPlanLockSource.Manual) : null);
        var batch = Batch(ImportFailurePolicy.SkipInvalidRows, Row(2, NationalId, "2026-09-28", "SAB"), Row(3, NationalId, "2026-10-05", "SAB"));

        var result = await ImportHandler().ProcessAsync(batch, TestContext.Current.CancellationToken);

        result.Rows[0].Outcome.ShouldBe(ImportRowOutcome.Error);
        result.Rows[0].Errors.Single().Code.ShouldBe(AttendanceDomainErrors.ShiftPeriodLocked);
        result.Rows[1].Outcome.ShouldBe(ImportRowOutcome.Inserted);
        _shifts.Received(1).Add(Arg.Is<ShiftAssignment>(a => a.Date == new DateOnly(2026, 10, 5)));
    }

    [Fact]
    public async Task Import_all_or_nothing_persists_nothing_when_a_row_fails()
    {
        var batch = Batch(ImportFailurePolicy.AllOrNothing, Row(2, NationalId, "2026-10-05", "SAB"), Row(3, NationalId, "2026-10-06", "YOK"));

        var result = await ImportHandler().ProcessAsync(batch, TestContext.Current.CancellationToken);

        result.Applied.ShouldBeFalse();
        result.Rows.Select(r => r.Outcome).ShouldBe([ImportRowOutcome.Skipped, ImportRowOutcome.Error]);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Template_workbook_has_shift_code_dropdown()
    {
        var template = new ShiftPlanImportTemplate(_definitions);

        var columns = await template.GetColumnsAsync(TestContext.Current.CancellationToken);

        template.Code.ShouldBe("shift-plan");
        var shiftCode = columns.Single(c => c.Key == ShiftPlanImportColumns.ShiftCode);
        shiftCode.Options.ShouldBe(["GEC", "SAB", "WEEKLY_REST", "REST_DAY", "PUBLIC_HOLIDAY"]);
        columns.Single(c => c.IsMatchKey).Key.ShouldBe(ShiftPlanImportColumns.NationalId);
        template.GetSampleRows(columns).ShouldContain(r => r.IsValid);
    }
}

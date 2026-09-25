using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NSubstitute;
using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Modules.Attendance.Domain.WorkSchedules;
using Sense.Modules.Attendance.Infrastructure.Reporting;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Reporting.Application;
using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.Export;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.Reporting;

/// <summary>
/// Z4.1a zaman raporları (R1): Hesaplanamayanlar, Vardiya Atanmayanlar, Günlük durum listeleri. Günlük sorgu ve depolar
/// taklit edilir (kapsam/tarih argümanı doğrulanır); Excel/PDF üretimi gerçek ClosedXML/QuestPDF ile yapılır.
/// </summary>
public sealed class TimeReportProvidersTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 9, 21); // Pazartesi

    private static readonly OrgUnitTypeInfo CompanyLevel = new(Guid.NewGuid(), "Company", "Şirket", 0);
    private static readonly OrgUnitInfo Acme = new(Guid.NewGuid(), CompanyLevel.Id, null, "Acme");

    private readonly IDailyAttendanceQuery _query = Substitute.For<IDailyAttendanceQuery>();
    private readonly ITenantLocalClock _clock = new FixedLocalClock(new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc));
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();
    private readonly IOrgUnitLookup _orgUnits = Substitute.For<IOrgUnitLookup>();
    private readonly IEmployeeReportReader _employees = Substitute.For<IEmployeeReportReader>();
    private readonly IShiftAssignmentRepository _shifts = Substitute.For<IShiftAssignmentRepository>();
    private readonly IWorkScheduleRepository _schedules = Substitute.For<IWorkScheduleRepository>();
    private readonly IEmployeeWorkScheduleAssignmentRepository _scheduleAssignments = Substitute.For<IEmployeeWorkScheduleAssignmentRepository>();
    private readonly IReportExporter _exporter = new ExcelReportExporter();
    private readonly ISimplePdfWriter _pdf = new QuestPdfSimpleWriter();
    private readonly IStringLocalizer<SharedResource> _localizer;

    public TimeReportProvidersTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization(o => o.ResourcesPath = "Resources");
        _localizer = services.BuildServiceProvider().GetRequiredService<IStringLocalizer<SharedResource>>();
        System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("tr-TR");

        _dataScope.GetVisibleEmployeeIdsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((IReadOnlySet<Guid>?)null);
        _orgUnits.ListActiveTypesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([CompanyLevel]);
        _orgUnits.ListActiveUnitsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([Acme]);
        _orgUnits.GetSelfAndDescendantIdsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IReadOnlyCollection<Guid>>().ToList());
        _schedules.FindDefaultAsync(Arg.Any<CancellationToken>()).Returns(WorkSchedule.CreateDefault(TenantId, "Genel"));
        _scheduleAssignments.ListCoveringRangeAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _shifts.ListForRangeAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>())
            .Returns([]);
    }

    private IncalculableReportProvider Incalculable() => new(_query, _clock, _exporter, _pdf, _localizer);

    private DailyStatusListsReportProvider DailyLists() => new(_query, _clock, _exporter, _pdf, _localizer);

    private NoShiftAssignedReportProvider NoShift() =>
        new(_shifts, _schedules, _scheduleAssignments, _dataScope, _orgUnits, _employees, _exporter, _pdf, _localizer);

    private static EmployeeReportRow Row(Guid id, string first, string last, DateOnly? hire = null, DateOnly? termination = null) =>
        new(id, first, last, null, hire ?? new DateOnly(2020, 1, 1), termination, "Active", Acme.Id, null, null, null, "FullTime", null, "Female", null,
            new Dictionary<Guid, Guid> { [CompanyLevel.Id] = Acme.Id });

    private static DailyAttendanceRowDto DailyRow(Guid id, string name, TimeOnly? checkIn = null, TimeOnly? checkOut = null) =>
        new(id, name, null, Day, checkIn, checkOut, 480, 30, ["lateArrival"], "ANNUAL");

    private void QueryReturns(DailyStatus status, params DailyAttendanceRowDto[] rows) =>
        _query.ListAsync(TenantId, Arg.Any<DateOnly>(), status, Arg.Any<Guid?>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new PagedResult<DailyAttendanceRowDto>(rows, call.ArgAt<int>(5), call.ArgAt<int>(6), rows.Length)));

    private static Dictionary<string, string> Params(params (string Key, string Value)[] values) =>
        values.ToDictionary(v => v.Key, v => v.Value);

    private static IXLWorksheet FirstSheet(ReportFileResult file) => new XLWorkbook(new MemoryStream(file.Content)).Worksheets.First();

    private static string[] Headers(IXLWorksheet sheet) => sheet.Row(1).CellsUsed().Select(c => c.GetString()).ToArray();

    private static void ShouldBePdf(ReportFileResult file)
    {
        file.ContentType.ShouldBe("application/pdf");
        file.FileName.ShouldEndWith(".pdf");
        System.Text.Encoding.ASCII.GetString(file.Content, 0, 5).ShouldBe("%PDF-");
    }

    // ---- Hesaplanamayanlar ----

    [Fact]
    public async Task Incalculable_Excel_has_expected_columns()
    {
        var id = Guid.NewGuid();
        QueryReturns(DailyStatus.Incalculable, DailyRow(id, "Ayşe Yılmaz"));

        var file = await Incalculable().GenerateAsync(TenantId, Params(("from", "2026-09-21"), ("to", "2026-09-21")), TestContext.Current.CancellationToken);

        var sheet = FirstSheet(file);
        Headers(sheet).ShouldBe(["Çalışan", "Tarih", "Giriş", "Çıkış", "Çalışılan dakika", "Fazla mesai dakikası", "Uyuşmazlıklar", "İzin türü"]);
        sheet.Cell(2, 1).GetString().ShouldBe("Ayşe Yılmaz");
        sheet.Cell(2, 2).GetFormattedString().ShouldBe("21.09.2026");
    }

    [Fact]
    public async Task Incalculable_Pdf_when_format_pdf()
    {
        QueryReturns(DailyStatus.Incalculable, DailyRow(Guid.NewGuid(), "Ayşe Yılmaz"));

        var file = await Incalculable().GenerateAsync(TenantId, Params(("date", "2026-09-21"), ("format", "pdf")), TestContext.Current.CancellationToken);

        ShouldBePdf(file);
    }

    [Fact]
    public async Task Incalculable_Scope_and_date_param_respected()
    {
        var unit = Guid.NewGuid();
        QueryReturns(DailyStatus.Incalculable);

        await Incalculable().GenerateAsync(
            TenantId, Params(("date", "2026-09-21"), ("orgUnitIds", unit.ToString())), TestContext.Current.CancellationToken);

        await _query.Received(1).ListAsync(TenantId, Day, DailyStatus.Incalculable, unit, true, 1, Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _query.DidNotReceive().ListAsync(TenantId, Arg.Is<DateOnly>(d => d != Day), Arg.Any<DailyStatus>(), Arg.Any<Guid?>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Incalculable_Empty_result_produces_header_only_file()
    {
        QueryReturns(DailyStatus.Incalculable);

        var excel = await Incalculable().GenerateAsync(TenantId, Params(("date", "2026-09-21")), TestContext.Current.CancellationToken);
        var pdf = await Incalculable().GenerateAsync(TenantId, Params(("date", "2026-09-21"), ("format", "pdf")), TestContext.Current.CancellationToken);

        var sheet = FirstSheet(excel);
        Headers(sheet).Length.ShouldBe(8);
        sheet.LastRowUsed()!.RowNumber().ShouldBe(1);
        ShouldBePdf(pdf);
    }

    [Fact]
    public async Task Incalculable_defaults_to_tenant_today_and_paginates()
    {
        var rows = Enumerable.Range(0, 3).Select(i => DailyRow(Guid.NewGuid(), $"Kişi {i}")).ToArray();
        _query.ListAsync(TenantId, Day, DailyStatus.Incalculable, null, true, 1, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<DailyAttendanceRowDto>(rows.Take(2).ToList(), 1, 2, 3));
        _query.ListAsync(TenantId, Day, DailyStatus.Incalculable, null, true, 2, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<DailyAttendanceRowDto>(rows.Skip(2).ToList(), 2, 2, 3));

        var file = await Incalculable().GenerateAsync(TenantId, Params(), TestContext.Current.CancellationToken);

        FirstSheet(file).LastRowUsed()!.RowNumber().ShouldBe(4);
    }

    // ---- Günlük durum listeleri ----

    [Theory]
    [InlineData("late", DailyStatus.Late)]
    [InlineData("earlyLeave", DailyStatus.EarlyLeave)]
    [InlineData("absent", DailyStatus.Absent)]
    [InlineData("overtime", DailyStatus.Overtime)]
    [InlineData("undertime", DailyStatus.Undertime)]
    [InlineData("onLeave", DailyStatus.OnLeave)]
    [InlineData("arrived", DailyStatus.Arrived)]
    [InlineData("incalculable", DailyStatus.Incalculable)]
    public async Task DailyStatus_lists_the_requested_status(string parameter, DailyStatus expected)
    {
        QueryReturns(expected, DailyRow(Guid.NewGuid(), "Ayşe Yılmaz", new TimeOnly(9, 30), new TimeOnly(18, 0)));

        var file = await DailyLists().GenerateAsync(TenantId, Params(("date", "2026-09-21"), ("status", parameter)), TestContext.Current.CancellationToken);

        var sheet = FirstSheet(file);
        Headers(sheet).ShouldBe(["Çalışan", "Tarih", "Giriş", "Çıkış", "Çalışılan dakika", "Fazla mesai dakikası", "Uyuşmazlıklar", "İzin türü"]);
        sheet.Cell(2, 1).GetString().ShouldBe("Ayşe Yılmaz");
        sheet.Cell(2, 3).GetFormattedString().ShouldBe("09:30");
    }

    [Fact]
    public async Task DailyStatus_Pdf_when_format_pdf()
    {
        QueryReturns(DailyStatus.Late, DailyRow(Guid.NewGuid(), "Ayşe Yılmaz", new TimeOnly(9, 30), null));

        var file = await DailyLists().GenerateAsync(TenantId, Params(("date", "2026-09-21"), ("status", "late"), ("format", "pdf")), TestContext.Current.CancellationToken);

        ShouldBePdf(file);
    }

    [Fact]
    public async Task DailyStatus_Scope_and_date_param_respected()
    {
        var unit = Guid.NewGuid();
        QueryReturns(DailyStatus.Absent);

        await DailyLists().GenerateAsync(
            TenantId, Params(("date", "2026-09-21"), ("status", "absent"), ("orgUnitIds", unit.ToString())), TestContext.Current.CancellationToken);

        await _query.Received(1).ListAsync(TenantId, Day, DailyStatus.Absent, unit, true, 1, Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DailyStatus_Empty_result_produces_header_only_file()
    {
        QueryReturns(DailyStatus.Absent);

        var file = await DailyLists().GenerateAsync(TenantId, Params(("date", "2026-09-21"), ("status", "absent")), TestContext.Current.CancellationToken);

        FirstSheet(file).LastRowUsed()!.RowNumber().ShouldBe(1);
    }

    [Fact]
    public async Task DailyStatus_unknown_status_is_rejected()
    {
        await Should.ThrowAsync<InvalidOperationException>(() =>
            DailyLists().GenerateAsync(TenantId, Params(("date", "2026-09-21"), ("status", "nonsense")), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void DailyStatus_announces_status_and_format_as_select_parameters()
    {
        var parameters = DailyLists().Parameters;

        var status = parameters.Single(p => p.Key == "status");
        status.Kind.ShouldBe(ReportParameterKind.Select);
        status.IsRequired.ShouldBeTrue();
        status.Options.ShouldBe(["incalculable", "arrived", "late", "earlyLeave", "absent", "overtime", "undertime", "onLeave"]);
        var format = parameters.Single(p => p.Key == "format");
        format.Kind.ShouldBe(ReportParameterKind.Select);
        format.Options.ShouldBe(["xlsx", "pdf"]);
    }

    [Fact]
    public void Invalid_status_returns_validation_error()
    {
        var result = ReportParameterValidation.Validate(DailyLists().Parameters, Params(("status", "nonsense")));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ReportingErrors.ParameterInvalid);
    }

    [Fact]
    public void Missing_status_returns_validation_error()
    {
        var result = ReportParameterValidation.Validate(DailyLists().Parameters, Params(("date", "2026-09-21")));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ReportingErrors.ParameterRequired);
    }

    [Fact]
    public void Valid_status_and_format_pass_validation()
    {
        ReportParameterValidation.Validate(DailyLists().Parameters, Params(("status", "earlyLeave"), ("format", "pdf"))).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Anomalies_column_uses_localized_labels()
    {
        QueryReturns(DailyStatus.Late, DailyRow(Guid.NewGuid(), "Ayşe Yılmaz"));

        var file = await DailyLists().GenerateAsync(TenantId, Params(("date", "2026-09-21"), ("status", "late")), TestContext.Current.CancellationToken);

        FirstSheet(file).Cell(2, 7).GetString().ShouldBe("Geç giriş");
    }

    // ---- Vardiya atanmayanlar ----

    [Fact]
    public async Task NoShift_Excel_has_expected_columns_and_counts_unassigned_working_days()
    {
        var id = Guid.NewGuid();
        _employees.ListAsync(TenantId, Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<IReadOnlyCollection<Guid>?>(), false, Arg.Any<CancellationToken>())
            .Returns([Row(id, "Ayşe", "Yılmaz")]);

        // Pzt 21 - Cmt 26 Eylül: Pzt-Cum çalışma günü (5), Cmt tatil -> 5 atanmamış gün.
        var file = await NoShift().GenerateAsync(TenantId, Params(("from", "2026-09-21"), ("to", "2026-09-26")), TestContext.Current.CancellationToken);

        var sheet = FirstSheet(file);
        Headers(sheet).ShouldBe(["Ad", "Soyad", "Şirket", "Atanmamış gün sayısı", "İlk atanmamış gün"]);
        sheet.Cell(2, 1).GetString().ShouldBe("Ayşe");
        sheet.Cell(2, 4).GetString().ShouldBe("5");
        sheet.Cell(2, 5).GetFormattedString().ShouldBe("21.09.2026");
    }

    [Fact]
    public async Task NoShift_ignores_days_with_any_assignment_including_non_work_day_types()
    {
        var id = Guid.NewGuid();
        var tenant = TenantId;
        _employees.ListAsync(tenant, Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<IReadOnlyCollection<Guid>?>(), false, Arg.Any<CancellationToken>())
            .Returns([Row(id, "Ayşe", "Yılmaz")]);
        var worked = ShiftAssignment.Create(tenant, id, Day, new TimeOnly(9, 0), new TimeOnly(18, 0), 60, false, null).Value;
        var restDay = ShiftAssignment.CreateDayOff(tenant, id, Day.AddDays(1), ShiftDayType.WeeklyRest).Value;
        _shifts.ListForRangeAsync(Day, Day.AddDays(4), Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>()).Returns([worked, restDay]);

        var file = await NoShift().GenerateAsync(TenantId, Params(("from", "2026-09-21"), ("to", "2026-09-25")), TestContext.Current.CancellationToken);

        var sheet = FirstSheet(file);
        sheet.Cell(2, 4).GetString().ShouldBe("3"); // Çar, Per, Cum
        sheet.Cell(2, 5).GetFormattedString().ShouldBe("23.09.2026");
    }

    [Fact]
    public async Task NoShift_omits_employees_fully_assigned_and_days_outside_employment()
    {
        var assigned = Guid.NewGuid();
        var newHire = Guid.NewGuid();
        _employees.ListAsync(TenantId, Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<IReadOnlyCollection<Guid>?>(), false, Arg.Any<CancellationToken>())
            .Returns([Row(assigned, "Dolu", "Plan"), Row(newHire, "Yeni", "Girişli", hire: Day.AddDays(4))]);
        _shifts.ListForRangeAsync(Day, Day.AddDays(4), Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>())
            .Returns(Enumerable.Range(0, 5).Select(i => ShiftAssignment.Create(TenantId, assigned, Day.AddDays(i), new TimeOnly(9, 0), new TimeOnly(18, 0), 60, false, null).Value).ToList());

        var file = await NoShift().GenerateAsync(TenantId, Params(("from", "2026-09-21"), ("to", "2026-09-25")), TestContext.Current.CancellationToken);

        var sheet = FirstSheet(file);
        sheet.LastRowUsed()!.RowNumber().ShouldBe(2);
        sheet.Cell(2, 1).GetString().ShouldBe("Yeni");
        sheet.Cell(2, 4).GetString().ShouldBe("1"); // yalnız işe giriş günü (Cum)
    }

    [Fact]
    public async Task NoShift_uses_the_employees_own_schedule_when_assigned()
    {
        var id = Guid.NewGuid();
        _employees.ListAsync(TenantId, Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<IReadOnlyCollection<Guid>?>(), false, Arg.Any<CancellationToken>())
            .Returns([Row(id, "Ayşe", "Yılmaz")]);
        var allOff = WorkSchedule.Create(TenantId, "Kapalı", Enum.GetValues<DayOfWeek>().Select(WorkScheduleDay.Off).ToList()).Value;
        _schedules.ListAsync(true, Arg.Any<CancellationToken>()).Returns([allOff]);
        _scheduleAssignments.ListCoveringRangeAsync(id, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([EmployeeWorkScheduleAssignment.Create(TenantId, id, allOff.Id, new DateOnly(2026, 1, 1), null, WorkScheduleAssignmentSource.Manual, null)]);

        var file = await NoShift().GenerateAsync(TenantId, Params(("from", "2026-09-21"), ("to", "2026-09-25")), TestContext.Current.CancellationToken);

        FirstSheet(file).LastRowUsed()!.RowNumber().ShouldBe(1);
    }

    [Fact]
    public async Task NoShift_Pdf_when_format_pdf()
    {
        _employees.ListAsync(TenantId, Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<IReadOnlyCollection<Guid>?>(), false, Arg.Any<CancellationToken>())
            .Returns([Row(Guid.NewGuid(), "Ayşe", "Yılmaz")]);

        var file = await NoShift().GenerateAsync(TenantId, Params(("from", "2026-09-21"), ("to", "2026-09-25"), ("format", "pdf")), TestContext.Current.CancellationToken);

        ShouldBePdf(file);
    }

    [Fact]
    public async Task NoShift_Scope_and_date_param_respected()
    {
        var visible = new HashSet<Guid> { Guid.NewGuid() };
        _dataScope.GetVisibleEmployeeIdsAsync("attendance.daily.view", Arg.Any<CancellationToken>()).Returns((IReadOnlySet<Guid>?)visible);
        _employees.ListAsync(TenantId, visible, Arg.Any<IReadOnlyCollection<Guid>?>(), false, Arg.Any<CancellationToken>()).Returns([]);

        await NoShift().GenerateAsync(TenantId, Params(("from", "2026-09-21"), ("to", "2026-09-25")), TestContext.Current.CancellationToken);

        await _employees.Received(1).ListAsync(TenantId, visible, Arg.Any<IReadOnlyCollection<Guid>?>(), false, Arg.Any<CancellationToken>());
        await _shifts.DidNotReceive().ListForRangeAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Is<IReadOnlyCollection<Guid>?>(ids => ids != null && ids.Count > 0), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoShift_Empty_result_produces_header_only_file()
    {
        _employees.ListAsync(TenantId, Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<IReadOnlyCollection<Guid>?>(), false, Arg.Any<CancellationToken>())
            .Returns([]);

        var file = await NoShift().GenerateAsync(TenantId, Params(("from", "2026-09-21"), ("to", "2026-09-25")), TestContext.Current.CancellationToken);

        var sheet = FirstSheet(file);
        Headers(sheet).Length.ShouldBe(5);
        sheet.LastRowUsed()!.RowNumber().ShouldBe(1);
    }

    // ---- Katalog sözleşmesi ----

    [Fact]
    public void Providers_advertise_time_category_and_daily_view_permission()
    {
        IReportProvider[] providers = [Incalculable(), DailyLists(), NoShift()];

        providers.Select(p => p.Key).ShouldBe(["attendance.time.incalculable", "attendance.time.daily_status", "attendance.time.no_shift_assigned"]);
        providers.ShouldAllBe(p => p.CategoryKey == ReportCategories.Time && p.RequiredPermission == "attendance.daily.view");
    }
}

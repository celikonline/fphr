using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NSubstitute;
using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Punches;
using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Sense.Modules.Attendance.Domain.Terminals;
using Sense.Modules.Attendance.Infrastructure.Reporting;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.Export;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.Reporting;

/// <summary>
/// Z4.1b okutma tabanlı zaman raporları (R2): Elle Girilen Kayıtlar, İçerdekiler, Mola, Ara Kayıtlar. Okutma depoları taklit edilir;
/// Excel/PDF üretimi gerçek ClosedXML/QuestPDF ile yapılır. Kiracı saat dilimi UTC+3 (Europe/Istanbul).
/// </summary>
public sealed class PunchReportProvidersTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 9, 21);
    private static readonly Guid Ayse = Guid.NewGuid();
    private static readonly Guid Mehmet = Guid.NewGuid();
    private static readonly Guid ActorUser = Guid.NewGuid();
    private static readonly Terminal MainGate = Terminal.Create(TenantId, "T1", "Ana Kapı", null, null, TerminalDirectionMode.Both, "hash", "prefix").Value;
    private static readonly Guid Terminal1 = MainGate.Id;

    private static readonly OrgUnitTypeInfo CompanyLevel = new(Guid.NewGuid(), "Company", "Şirket", 0);
    private static readonly OrgUnitInfo Acme = new(Guid.NewGuid(), CompanyLevel.Id, null, "Acme");

    private readonly Sense.Modules.Attendance.Domain.IPunchRepository _punches = Substitute.For<Sense.Modules.Attendance.Domain.IPunchRepository>();
    private readonly IPunchReportReader _reader = Substitute.For<IPunchReportReader>();
    private readonly ITerminalRepository _terminals = Substitute.For<ITerminalRepository>();
    private readonly IPlannedDayResolver _planner = Substitute.For<IPlannedDayResolver>();
    private readonly ITenantTimeZoneLookup _zones = Substitute.For<ITenantTimeZoneLookup>();
    private readonly IUserProfileLookup _users = Substitute.For<IUserProfileLookup>();
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();
    private readonly IOrgUnitLookup _orgUnits = Substitute.For<IOrgUnitLookup>();
    private readonly IEmployeeReportReader _employees = Substitute.For<IEmployeeReportReader>();
    private readonly ITenantLocalClock _clock = new FixedLocalClock(new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 21, 15, 0, 0));
    private readonly IReportExporter _exporter = new ExcelReportExporter();
    private readonly ISimplePdfWriter _pdf = new QuestPdfSimpleWriter();
    private readonly IStringLocalizer<SharedResource> _localizer;

    public PunchReportProvidersTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization(o => o.ResourcesPath = "Resources");
        _localizer = services.BuildServiceProvider().GetRequiredService<IStringLocalizer<SharedResource>>();
        System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("tr-TR");

        _zones.FindTimeZoneIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns("Europe/Istanbul");
        _dataScope.GetVisibleEmployeeIdsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((IReadOnlySet<Guid>?)null);
        _orgUnits.ListActiveTypesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([CompanyLevel]);
        _orgUnits.ListActiveUnitsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([Acme]);
        _orgUnits.GetSelfAndDescendantIdsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IReadOnlyCollection<Guid>>().ToList());
        _employees.ListAsync(TenantId, Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var visible = call.ArgAt<IReadOnlyCollection<Guid>?>(1);
                IReadOnlyList<EmployeeReportRow> all = [Row(Ayse, "Ayşe", "Yılmaz"), Row(Mehmet, "Mehmet", "Demir")];
                return Task.FromResult<IReadOnlyList<EmployeeReportRow>>(all.Where(r => visible is null || visible.Contains(r.EmployeeId)).ToList());
            });
        _users.GetAsync(TenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, UserProfileInfo> { [ActorUser] = new(ActorUser, null, "ik@acme.test", "İK Uzmanı", null, true) });
        _terminals.ListTerminalsAsync(Arg.Any<CancellationToken>())
            .Returns([MainGate]);
        _planner.ResolveAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<TimeOnly?>(), Arg.Any<CancellationToken>())
            .Returns(new PlannedDay(new TimeOnly(9, 0), new TimeOnly(18, 0), 480, 60));
    }

    private ManualEntriesReportProvider Manual() => new(_reader, _dataScope, _orgUnits, _employees, _zones, _users, _exporter, _pdf, _localizer);

    private CurrentlyInsideReportProvider Inside() => new(_punches, _terminals, _clock, _zones, _dataScope, _orgUnits, _employees, _exporter, _pdf, _localizer);

    private BreaksReportProvider Breaks() => new(_punches, _planner, _clock, _zones, _dataScope, _orgUnits, _employees, _exporter, _pdf, _localizer);

    private IntermediatePunchesReportProvider Intermediate() => new(_punches, _terminals, _clock, _zones, _dataScope, _orgUnits, _employees, _exporter, _pdf, _localizer);

    private static EmployeeReportRow Row(Guid id, string first, string last) =>
        new(id, first, last, null, new DateOnly(2020, 1, 1), null, "Active", Acme.Id, null, null, null, "FullTime", null, "Female", null,
            new Dictionary<Guid, Guid> { [CompanyLevel.Id] = Acme.Id });

    /// <summary>Yerel (UTC+3) saatten okutma; 21.09.2026 için.</summary>
    private static Punch P(Guid employee, int localHour, int localMinute, PunchDirection dir, PunchSource source = PunchSource.SelfService,
        bool manual = false, Guid? terminal = null, Guid? by = null, string? reason = null) =>
        Punch.Create(
            TenantId, employee, new DateTime(2026, 9, 21, localHour - 3, localMinute, 0, DateTimeKind.Utc), Day, dir, source, null, terminal, null, null, null,
            manual, by, reason, nowUtc: new DateTime(2026, 9, 22, 0, 0, 0, DateTimeKind.Utc)).Value;

    private void LiveReturns(params Punch[] punches) =>
        _punches.ListForRangeAsync(TenantId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<Punch>>(
                punches.Where(p => call.ArgAt<IReadOnlyCollection<Guid>?>(3)?.Contains(p.EmployeeId) ?? true).ToList()));

    private void ManualReturns(params Punch[] punches) =>
        _reader.ListManualForRangeAsync(TenantId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<Punch>>(
                punches.Where(p => call.ArgAt<IReadOnlyCollection<Guid>>(3).Contains(p.EmployeeId)).ToList()));

    private static Dictionary<string, string> Params(params (string Key, string Value)[] values) => values.ToDictionary(v => v.Key, v => v.Value);

    private static IXLWorksheet FirstSheet(ReportFileResult file) => new XLWorkbook(new MemoryStream(file.Content)).Worksheets.First();

    private static string[] Headers(IXLWorksheet sheet) => sheet.Row(1).CellsUsed().Select(c => c.GetString()).ToArray();

    private static void ShouldBePdf(ReportFileResult file)
    {
        file.ContentType.ShouldBe("application/pdf");
        file.FileName.ShouldEndWith(".pdf");
        System.Text.Encoding.ASCII.GetString(file.Content, 0, 5).ShouldBe("%PDF-");
    }

    private static readonly (string, string)[] DayRange = [("from", "2026-09-21"), ("to", "2026-09-21")];

    // ---- Elle Girilen Kayıtlar ----

    [Fact]
    public async Task Manual_Excel_has_expected_columns_and_local_time()
    {
        ManualReturns(P(Ayse, 9, 5, PunchDirection.In, PunchSource.Manual, manual: true, by: ActorUser, reason: "Kart unutuldu"));

        var file = await Manual().GenerateAsync(TenantId, Params(DayRange), TestContext.Current.CancellationToken);

        var sheet = FirstSheet(file);
        Headers(sheet).ShouldBe(
            ["Çalışan", "Tarih", "Okutma zamanı", "Yön", "Kaynak", "Giriş yapan", "Gerekçe", "Silindi", "Silen", "Silme gerekçesi"]);
        sheet.Cell(2, 1).GetString().ShouldBe("Ayşe Yılmaz");
        sheet.Cell(2, 3).GetDateTime().ShouldBe(new DateTime(2026, 9, 21, 9, 5, 0));
        sheet.Cell(2, 4).GetString().ShouldBe("Giriş");
        sheet.Cell(2, 5).GetString().ShouldBe("Elle");
        sheet.Cell(2, 6).GetString().ShouldBe("İK Uzmanı");
        sheet.Cell(2, 7).GetString().ShouldBe("Kart unutuldu");
        sheet.Cell(2, 8).GetString().ShouldBe("Hayır");
    }

    [Fact]
    public async Task Manual_marks_deleted_entries_with_deleter_and_reason()
    {
        var deleted = P(Ayse, 9, 5, PunchDirection.In, PunchSource.Request, manual: true, by: ActorUser, reason: "Talep");
        deleted.SoftDelete(ActorUser, "Yanlış giriş", new DateTime(2026, 9, 21, 10, 0, 0, DateTimeKind.Utc)).IsSuccess.ShouldBeTrue();
        ManualReturns(deleted);

        var file = await Manual().GenerateAsync(TenantId, Params(DayRange), TestContext.Current.CancellationToken);

        var sheet = FirstSheet(file);
        sheet.Cell(2, 5).GetString().ShouldBe("Talep");
        sheet.Cell(2, 8).GetString().ShouldBe("Evet");
        sheet.Cell(2, 9).GetString().ShouldBe("İK Uzmanı");
        sheet.Cell(2, 10).GetString().ShouldBe("Yanlış giriş");
    }

    [Fact]
    public async Task Manual_respects_data_scope()
    {
        _dataScope.GetVisibleEmployeeIdsAsync("attendance.daily.view", Arg.Any<CancellationToken>()).Returns((IReadOnlySet<Guid>?)new HashSet<Guid> { Ayse });
        ManualReturns(P(Ayse, 9, 0, PunchDirection.In, PunchSource.Manual, manual: true), P(Mehmet, 9, 0, PunchDirection.In, PunchSource.Manual, manual: true));

        var file = await Manual().GenerateAsync(TenantId, Params(DayRange), TestContext.Current.CancellationToken);

        var sheet = FirstSheet(file);
        sheet.LastRowUsed()!.RowNumber().ShouldBe(2);
        sheet.Cell(2, 1).GetString().ShouldBe("Ayşe Yılmaz");
        await _reader.Received(1).ListManualForRangeAsync(
            TenantId, Day, Day, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(Ayse)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Manual_empty_result_has_header_only_and_pdf_works()
    {
        ManualReturns();

        var excel = await Manual().GenerateAsync(TenantId, Params(DayRange), TestContext.Current.CancellationToken);
        var pdf = await Manual().GenerateAsync(TenantId, Params(("from", "2026-09-21"), ("to", "2026-09-21"), ("format", "pdf")), TestContext.Current.CancellationToken);

        var sheet = FirstSheet(excel);
        Headers(sheet).Length.ShouldBe(10);
        sheet.LastRowUsed()!.RowNumber().ShouldBe(1);
        ShouldBePdf(pdf);
    }

    [Fact]
    public async Task Manual_requires_date_range()
    {
        await Should.ThrowAsync<InvalidOperationException>(
            () => Manual().GenerateAsync(TenantId, Params(), TestContext.Current.CancellationToken));
    }

    // ---- İçerdekiler ----

    [Fact]
    public async Task Inside_lists_only_employees_whose_last_punch_is_in()
    {
        LiveReturns(
            P(Ayse, 9, 0, PunchDirection.In, PunchSource.Terminal, terminal: Terminal1),
            P(Mehmet, 8, 30, PunchDirection.In), P(Mehmet, 12, 0, PunchDirection.Out));

        var file = await Inside().GenerateAsync(TenantId, Params(), TestContext.Current.CancellationToken);

        var sheet = FirstSheet(file);
        Headers(sheet).ShouldBe(["Çalışan", "Birim", "İlk giriş", "Son okutma", "Kaynak", "Terminal"]);
        sheet.LastRowUsed()!.RowNumber().ShouldBe(2);
        sheet.Cell(2, 1).GetString().ShouldBe("Ayşe Yılmaz");
        sheet.Cell(2, 2).GetString().ShouldBe("Acme");
        sheet.Cell(2, 3).GetString().ShouldBe("09:00");
        sheet.Cell(2, 4).GetString().ShouldBe("09:00");
        sheet.Cell(2, 5).GetString().ShouldBe("Terminal");
    }

    [Fact]
    public async Task Inside_ignores_deleted_out_punch_and_uses_tenant_local_today()
    {
        var deletedOut = P(Ayse, 17, 0, PunchDirection.Out);
        deletedOut.SoftDelete(ActorUser, "Hatalı", new DateTime(2026, 9, 21, 15, 0, 0, DateTimeKind.Utc)).IsSuccess.ShouldBeTrue();
        LiveReturns(P(Ayse, 9, 0, PunchDirection.In), deletedOut);

        var file = await Inside().GenerateAsync(TenantId, Params(), TestContext.Current.CancellationToken);

        FirstSheet(file).Cell(2, 1).GetString().ShouldBe("Ayşe Yılmaz");
        await _punches.Received(1).ListForRangeAsync(TenantId, Day, Day, Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Inside_scope_pdf_and_empty()
    {
        _dataScope.GetVisibleEmployeeIdsAsync("attendance.daily.view", Arg.Any<CancellationToken>()).Returns((IReadOnlySet<Guid>?)new HashSet<Guid> { Mehmet });
        LiveReturns(P(Ayse, 9, 0, PunchDirection.In));

        var excel = await Inside().GenerateAsync(TenantId, Params(), TestContext.Current.CancellationToken);
        var pdf = await Inside().GenerateAsync(TenantId, Params(("format", "pdf")), TestContext.Current.CancellationToken);

        FirstSheet(excel).LastRowUsed()!.RowNumber().ShouldBe(1);
        ShouldBePdf(pdf);
    }

    // ---- Mola ----

    [Fact]
    public async Task Breaks_total_is_out_to_in_gap_minutes()
    {
        LiveReturns(
            P(Ayse, 9, 0, PunchDirection.In), P(Ayse, 12, 0, PunchDirection.Out), P(Ayse, 12, 45, PunchDirection.In),
            P(Ayse, 15, 0, PunchDirection.Out), P(Ayse, 15, 10, PunchDirection.In), P(Ayse, 18, 0, PunchDirection.Out));

        var file = await Breaks().GenerateAsync(TenantId, Params(DayRange), TestContext.Current.CancellationToken);

        var sheet = FirstSheet(file);
        Headers(sheet).ShouldBe(
            ["Çalışan", "Tarih", "Mola sayısı", "Toplam mola (dk)", "En uzun mola (dk)", "Planlı mola (dk)", "Fark (dk)"]);
        sheet.Cell(2, 3).GetDouble().ShouldBe(2);
        sheet.Cell(2, 4).GetDouble().ShouldBe(55);
        sheet.Cell(2, 5).GetDouble().ShouldBe(45);
        sheet.Cell(2, 6).GetDouble().ShouldBe(60);
        sheet.Cell(2, 7).GetDouble().ShouldBe(-5);
    }

    [Fact]
    public async Task Breaks_single_45_minute_break_and_days_without_break_omitted()
    {
        LiveReturns(
            P(Ayse, 9, 0, PunchDirection.In), P(Ayse, 12, 0, PunchDirection.Out), P(Ayse, 12, 45, PunchDirection.In), P(Ayse, 18, 0, PunchDirection.Out),
            P(Mehmet, 9, 0, PunchDirection.In), P(Mehmet, 18, 0, PunchDirection.Out));

        var file = await Breaks().GenerateAsync(TenantId, Params(("date", "2026-09-21")), TestContext.Current.CancellationToken);

        var sheet = FirstSheet(file);
        sheet.LastRowUsed()!.RowNumber().ShouldBe(2);
        sheet.Cell(2, 1).GetString().ShouldBe("Ayşe Yılmaz");
        sheet.Cell(2, 4).GetDouble().ShouldBe(45);
    }

    [Fact]
    public async Task Breaks_ignores_deleted_punches_scope_pdf_and_empty()
    {
        var deleted = P(Ayse, 12, 45, PunchDirection.In);
        deleted.SoftDelete(ActorUser, "Hatalı", new DateTime(2026, 9, 21, 15, 0, 0, DateTimeKind.Utc)).IsSuccess.ShouldBeTrue();
        _dataScope.GetVisibleEmployeeIdsAsync("attendance.daily.view", Arg.Any<CancellationToken>()).Returns((IReadOnlySet<Guid>?)new HashSet<Guid> { Ayse });
        LiveReturns(P(Ayse, 9, 0, PunchDirection.In), P(Ayse, 12, 0, PunchDirection.Out), deleted, P(Mehmet, 9, 0, PunchDirection.In));

        var excel = await Breaks().GenerateAsync(TenantId, Params(DayRange), TestContext.Current.CancellationToken);
        var pdf = await Breaks().GenerateAsync(TenantId, Params(("date", "2026-09-21"), ("format", "pdf")), TestContext.Current.CancellationToken);

        FirstSheet(excel).LastRowUsed()!.RowNumber().ShouldBe(1);
        ShouldBePdf(pdf);
    }

    // ---- Ara Kayıtlar ----

    [Fact]
    public async Task Intermediate_excludes_first_in_and_last_out()
    {
        LiveReturns(
            P(Ayse, 9, 0, PunchDirection.In), P(Ayse, 12, 0, PunchDirection.Out, terminal: Terminal1, source: PunchSource.Terminal),
            P(Ayse, 12, 45, PunchDirection.In), P(Ayse, 18, 0, PunchDirection.Out),
            P(Mehmet, 9, 0, PunchDirection.In), P(Mehmet, 18, 0, PunchDirection.Out));

        var file = await Intermediate().GenerateAsync(TenantId, Params(DayRange), TestContext.Current.CancellationToken);

        var sheet = FirstSheet(file);
        Headers(sheet).ShouldBe(["Çalışan", "Tarih", "Sıra", "Zaman", "Yön", "Kaynak", "Terminal"]);
        sheet.LastRowUsed()!.RowNumber().ShouldBe(3);
        sheet.Cell(2, 3).GetDouble().ShouldBe(2);
        sheet.Cell(2, 4).GetString().ShouldBe("12:00");
        sheet.Cell(2, 5).GetString().ShouldBe("Çıkış");
        sheet.Cell(2, 7).GetString().ShouldBe("Ana Kapı");
        sheet.Cell(3, 4).GetString().ShouldBe("12:45");
        sheet.Cell(3, 5).GetString().ShouldBe("Giriş");
    }

    [Fact]
    public async Task Intermediate_scope_pdf_and_empty()
    {
        _dataScope.GetVisibleEmployeeIdsAsync("attendance.daily.view", Arg.Any<CancellationToken>()).Returns((IReadOnlySet<Guid>?)new HashSet<Guid> { Mehmet });
        LiveReturns(P(Ayse, 9, 0, PunchDirection.In), P(Ayse, 12, 0, PunchDirection.Out), P(Ayse, 18, 0, PunchDirection.Out));

        var excel = await Intermediate().GenerateAsync(TenantId, Params(DayRange), TestContext.Current.CancellationToken);
        var pdf = await Intermediate().GenerateAsync(TenantId, Params(("format", "pdf")), TestContext.Current.CancellationToken);

        FirstSheet(excel).LastRowUsed()!.RowNumber().ShouldBe(1);
        ShouldBePdf(pdf);
    }

    // ---- Katalog ----

    [Fact]
    public void Provider_keys_are_unique_time_category_and_permissioned()
    {
        IReportProvider[] providers = [Manual(), Inside(), Breaks(), Intermediate()];

        providers.Select(p => p.Key).Distinct().Count().ShouldBe(4);
        providers.Select(p => p.Key).ShouldBe(
            ["attendance.time.manual_entries", "attendance.time.currently_inside", "attendance.time.breaks", "attendance.time.intermediate_punches"]);
        providers.ShouldAllBe(p => p.CategoryKey == ReportCategories.Time && p.RequiredPermission == "attendance.daily.view");
        providers.Select(p => p.Key).ShouldNotContain(IncalculableReportProvider.ReportKey);
        providers.Select(p => p.Key).ShouldNotContain(DailyStatusListsReportProvider.ReportKey);
        providers.Select(p => p.Key).ShouldNotContain(NoShiftAssignedReportProvider.ReportKey);
    }
}

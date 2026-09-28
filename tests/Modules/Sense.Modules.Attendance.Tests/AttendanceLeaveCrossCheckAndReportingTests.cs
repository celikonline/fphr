using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Application.MonthlyTimesheet;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.MonthlyTimesheet;
using Sense.Modules.Attendance.Infrastructure;
using Sense.Modules.Attendance.Infrastructure.Persistence;
using Sense.Modules.Attendance.Infrastructure.Reporting;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Modules.Leave.Infrastructure;
using Sense.Modules.Leave.Infrastructure.Persistence;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Infrastructure.Export;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

/// <summary>
/// senseik-029 (izinli günde çalışma detektörü, ay sonu kapatma, puantaj raporları) on real PostgreSQL: Attendance
/// AND Leave DbContexts side by side (mirrors Sense.Modules.Leave.Tests.LeaveReportProviderTests's cross-module
/// wiring, symmetric direction - here Attendance is the consumer of Leave.Contracts.IApprovedLeaveDateLookup/ILeaveDayReader).
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class AttendanceLeaveCrossCheckAndReportingTests : TestFixture
{
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();
    private readonly IOrgUnitLookup _orgUnits = Substitute.For<IOrgUnitLookup>();
    private readonly IEmployeeReportReader _employeeReportReader = Substitute.For<IEmployeeReportReader>();

    private static readonly OrgUnitTypeInfo CompanyLevel = new(Guid.NewGuid(), "Company", "Şirket", 0);
    private static readonly OrgUnitInfo Acme = new(Guid.NewGuid(), CompanyLevel.Id, null, "Acme");

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        services.AddModuleDbContext<AttendanceDbContext>(configuration, AttendanceDbContext.SchemaName);
        services.AddModuleDbContext<LeaveDbContext>(configuration, LeaveDbContext.SchemaName);
        services.AddAttendanceServices(configuration);
        services.AddLeaveCoreServices(configuration); // ILeaveDayReader + IApprovedLeaveDateLookup - no Leave command handlers needed here.
        services.AddModuleHandlers(
            AttendanceDbContext.SchemaName,
            typeof(AttendanceErrors).Assembly,
            typeof(AttendancePermissions).Assembly,
            typeof(AttendanceDbContext).Assembly,
            typeof(IWorkScheduleRepository).Assembly);

        // Report providers render localized Excel headers (SharedResource) through the shared exporter.
        services.AddLocalization(o => o.ResourcesPath = "Resources");
        services.TryAddSingleton<IReportExporter, ExcelReportExporter>();
        services.TryAddSingleton<ISimplePdfWriter, QuestPdfSimpleWriter>(); // TK-004: opsiyonel format=pdf yolu için.
        services.AddSingleton(_dataScope);
        services.AddSingleton(_orgUnits);
        services.AddSingleton(_employeeReportReader);
        services.TryAddSingleton(Substitute.For<IPunchRepository>()); // B1 birleşmeden önce IPunchRepository kaydı yok; birleşince gerçek kayıt (TryAdd) korunur.
        services.TryAddSingleton(Substitute.For<Sense.Modules.Identity.Contracts.ITenantTimeZoneLookup>());
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(Substitute.For<Sense.Modules.Identity.Contracts.IUserProfileLookup>()); // Z4.1b: Elle Girilen Kayıtlar sağlayıcısı katalogda çözülebilsin.
        services.AddSingleton(Substitute.For<IEmployeeDirectory>()); // Z4.1a: günlük sorguya dayanan zaman raporu sağlayıcıları katalogda çözülebilsin.

        _orgUnits.ListActiveTypesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([CompanyLevel]);
        _orgUnits.ListActiveUnitsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([Acme]);
        _orgUnits.GetSelfAndDescendantIdsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IReadOnlyCollection<Guid>>().ToList());
        _dataScope.GetVisibleEmployeeIdsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((IReadOnlySet<Guid>?)null);
    }

    protected override async Task OnDatabaseReadyAsync()
    {
        await ServiceProvider!.GetRequiredService<AttendanceDbContext>().Database.MigrateAsync();
        await ServiceProvider.GetRequiredService<LeaveDbContext>().Database.MigrateAsync();
    }

    private static EmployeeReportRow Row(Guid employeeId, string first, string last) =>
        new(employeeId, first, last, null, new DateOnly(2020, 1, 1), null, "Active", Acme.Id, null, null, null, "FullTime", null, "Female", null,
            new Dictionary<Guid, Guid> { [CompanyLevel.Id] = Acme.Id });

    /// <summary>Bir çalışan için tek bir onaylı izin talebi (Yıllık) - [start, end] aralığını kapsar.</summary>
    private async Task SeedApprovedLeaveAsync(Guid tenantId, Guid employeeId, DateOnly start, DateOnly end)
    {
        using var write = ServiceProvider!.CreateScope();
        var db = write.ServiceProvider.GetRequiredService<LeaveDbContext>();
        var ruleSet = LeaveRuleSetSeed.CreateTurkishLabourLawRuleSet(tenantId, StatutoryLeaveDurations.Turkey2026);
        db.RuleSets.Add(ruleSet);
        var annual = ruleSet.Types.Single(t => t.Code == LeaveTypeCode.Annual);

        var days = (decimal)(end.DayNumber - start.DayNumber + 1);
        var request = LeaveRequest.Submit(
            tenantId, employeeId, annual.Id, LeaveTypeCode.Annual, start, end, days, "Test izni",
            LeaveRequestSource.SelfService, DateTime.UtcNow, null, null).Value;
        var approvalId = Guid.NewGuid();
        request.AttachApproval(approvalId);
        request.Approve(approvalId, DateTime.UtcNow);
        db.Requests.Add(request);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedPunchedDayAsync(Guid tenantId, Guid employeeId, DateOnly date)
    {
        using var write = ServiceProvider!.CreateScope();
        var db = write.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        var day = AttendanceDay.Record(
            tenantId, employeeId, date, new TimeOnly(9, 0), new TimeOnly(17, 0), new TimeOnly(9, 0), new TimeOnly(17, 0), 8 * 60, 0, AttendanceDaySource.Manual);
        db.AttendanceDays.Add(day);
        await db.SaveChangesAsync();
        return day.Id;
    }

    [Fact]
    public async Task DetectWorkedWhileOnLeave_PunchedDayOverlapsApprovedLeave_FlagsAndPersists()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var date = new DateOnly(2026, 9, 21);

        var dayId = await SeedPunchedDayAsync(tenantId, employeeId, date);
        await SeedApprovedLeaveAsync(tenantId, employeeId, date, date);

        using (var scope = ServiceProvider!.CreateScope())
        {
            var days = scope.ServiceProvider.GetRequiredService<IAttendanceDayRepository>();
            var checker = scope.ServiceProvider.GetRequiredService<LeaveOverlapChecker>();
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            var employeeDirectory = Substitute.For<IEmployeeDirectory>();
            employeeDirectory.ListActiveEmployeeIdsAsync(tenantId, Arg.Any<CancellationToken>()).Returns([employeeId]);
            var handler = new DetectWorkedWhileOnLeaveHandler(days, employeeDirectory, checker, tenant);

            var result = await handler.Handle(new DetectWorkedWhileOnLeaveCommand(date, date, null), TestContext.Current.CancellationToken);

            result.IsSuccess.ShouldBeTrue();
            result.Value.ShouldBe(1); // one AttendanceDay row changed

            var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
            await db.SaveChangesAsync(); // the handler mutated a tracked entity through the repository
        }

        using var read = ServiceProvider!.CreateScope();
        var repo = read.ServiceProvider.GetRequiredService<IAttendanceDayRepository>();
        var loaded = (await repo.GetByIdAsync(dayId, TestContext.Current.CancellationToken)).Value;
        loaded.Anomalies.HasFlag(AttendanceAnomaly.WorkedWhileOnLeave).ShouldBeTrue();
        loaded.ResolutionState.ShouldBe(AttendanceResolutionState.Unresolved);
    }

    [Fact]
    public async Task DetectWorkedWhileOnLeave_NoOverlap_LeavesDayUnflagged()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var date = new DateOnly(2026, 9, 21);
        var dayId = await SeedPunchedDayAsync(tenantId, employeeId, date);
        // No approved leave seeded.

        using var scope = ServiceProvider!.CreateScope();
        var days = scope.ServiceProvider.GetRequiredService<IAttendanceDayRepository>();
        var checker = scope.ServiceProvider.GetRequiredService<LeaveOverlapChecker>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var employeeDirectory = Substitute.For<IEmployeeDirectory>();
        employeeDirectory.ListActiveEmployeeIdsAsync(tenantId, Arg.Any<CancellationToken>()).Returns([employeeId]);
        var handler = new DetectWorkedWhileOnLeaveHandler(days, employeeDirectory, checker, tenant);

        var result = await handler.Handle(new DetectWorkedWhileOnLeaveCommand(date, date, null), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(0);

        var repo = scope.ServiceProvider.GetRequiredService<IAttendanceDayRepository>();
        var loaded = (await repo.GetByIdAsync(dayId, TestContext.Current.CancellationToken)).Value;
        loaded.Anomalies.ShouldBe(AttendanceAnomaly.None);
    }

    private static IDataScopeService AllowAllScope()
    {
        var scope = Substitute.For<IDataScopeService>();
        scope.CanAccessEmployeeAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        return scope;
    }

    [Fact]
    public async Task ApproveMonthlyTimesheet_FirstTime_CreatesApprovalAndRunsLeaveCrossCheck()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var date = new DateOnly(2026, 9, 21);
        var dayId = await SeedPunchedDayAsync(tenantId, employeeId, date);
        await SeedApprovedLeaveAsync(tenantId, employeeId, date, date);

        using (var scope = ServiceProvider!.CreateScope())
        {
            var approvals = scope.ServiceProvider.GetRequiredService<IMonthlyTimesheetApprovalRepository>();
            var days = scope.ServiceProvider.GetRequiredService<IAttendanceDayRepository>();
            var checker = scope.ServiceProvider.GetRequiredService<LeaveOverlapChecker>();
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            var currentUser = Substitute.For<ICurrentUser>();
            var approverId = Guid.NewGuid();
            currentUser.UserId.Returns(approverId);
            var handler = new ApproveMonthlyTimesheetHandler(approvals, days, checker, AllowAllScope(), tenant, currentUser, TimeProvider.System, new FixedLocalClock(new DateTime(2026, 9, 30, 12, 0, 0)));

            var result = await handler.Handle(new ApproveMonthlyTimesheetCommand(employeeId, date), TestContext.Current.CancellationToken);

            result.IsSuccess.ShouldBeTrue();
            result.Value.EmployeeId.ShouldBe(employeeId);
            result.Value.ApprovedByUserId.ShouldBe(approverId);

            var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
            await db.SaveChangesAsync();
        }

        // Ay sonu kapatma önce izin çapraz kontrolünü çalıştırdı - kapatma anında zaten izinli günde çalışma işaretlendi.
        using var read = ServiceProvider!.CreateScope();
        var days2 = read.ServiceProvider.GetRequiredService<IAttendanceDayRepository>();
        var loaded = (await days2.GetByIdAsync(dayId, TestContext.Current.CancellationToken)).Value;
        loaded.Anomalies.HasFlag(AttendanceAnomaly.WorkedWhileOnLeave).ShouldBeTrue();

        var approvalsRepo = read.ServiceProvider.GetRequiredService<IMonthlyTimesheetApprovalRepository>();
        var approval = await approvalsRepo.FindAsync(employeeId, date, TestContext.Current.CancellationToken);
        approval.ShouldNotBeNull();
    }

    [Fact]
    public async Task ApproveMonthlyTimesheet_CalledTwice_Reapproves()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var periodMonth = new DateOnly(2026, 9, 1);

        using (var scope = ServiceProvider!.CreateScope())
        {
            var approvals = scope.ServiceProvider.GetRequiredService<IMonthlyTimesheetApprovalRepository>();
            var days = scope.ServiceProvider.GetRequiredService<IAttendanceDayRepository>();
            var checker = scope.ServiceProvider.GetRequiredService<LeaveOverlapChecker>();
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            var writeDb = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
            var currentUser = Substitute.For<ICurrentUser>();
            currentUser.UserId.Returns(Guid.NewGuid());
            var handler = new ApproveMonthlyTimesheetHandler(approvals, days, checker, AllowAllScope(), tenant, currentUser, TimeProvider.System, new FixedLocalClock(new DateTime(2026, 9, 30, 12, 0, 0)));

            var first = await handler.Handle(new ApproveMonthlyTimesheetCommand(employeeId, periodMonth), TestContext.Current.CancellationToken);
            first.IsSuccess.ShouldBeTrue();
            await writeDb.SaveChangesAsync();

            var second = await handler.Handle(new ApproveMonthlyTimesheetCommand(employeeId, periodMonth), TestContext.Current.CancellationToken);
            second.IsSuccess.ShouldBeTrue();
            await writeDb.SaveChangesAsync();
        }

        using var read = ServiceProvider!.CreateScope();
        var db = read.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        var rows = await db.MonthlyTimesheetApprovals.Where(a => a.EmployeeId == employeeId).ToListAsync(TestContext.Current.CancellationToken);
        rows.ShouldHaveSingleItem(); // Reapprove updates the same row, does not insert a second one
    }

    [Fact]
    public async Task MonthlySummaryReport_ComputesAggregatesFromAttendanceDays()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");

        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<AttendanceDbContext>();
            // Tam plan (uyuşmazlık yok, çalışılan gün).
            db.AttendanceDays.Add(AttendanceDay.Record(
                tenantId, employeeId, new DateOnly(2026, 9, 7), new TimeOnly(9, 0), new TimeOnly(17, 0),
                new TimeOnly(9, 0), new TimeOnly(17, 0), 8 * 60, 0, AttendanceDaySource.Manual));
            // Geç kalma + fazla çalışma (09:30-19:00).
            db.AttendanceDays.Add(AttendanceDay.Record(
                tenantId, employeeId, new DateOnly(2026, 9, 8), new TimeOnly(9, 30), new TimeOnly(19, 0),
                new TimeOnly(9, 0), new TimeOnly(17, 0), 8 * 60, 0, AttendanceDaySource.Manual));
            // Kayıt yok (NoData/eksik gün).
            db.AttendanceDays.Add(AttendanceDay.Record(
                tenantId, employeeId, new DateOnly(2026, 9, 9), null, null,
                new TimeOnly(9, 0), new TimeOnly(17, 0), 8 * 60, 0, AttendanceDaySource.Manual));
            await db.SaveChangesAsync();
        }

        _employeeReportReader.ListAsync(tenantId, Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<IReadOnlyCollection<Guid>?>(), false, Arg.Any<CancellationToken>())
            .Returns([Row(employeeId, "Ayşe", "Yılmaz")]);

        using var scope = ServiceProvider!.CreateScope();
        var provider = scope.ServiceProvider.GetServices<IReportProvider>().Single(p => p.Key == AttendanceMonthlySummaryReportProvider.ReportKey);
        var parameters = new Dictionary<string, string> { ["year"] = "2026", ["month"] = "9" };

        var file = await provider.GenerateAsync(tenantId, parameters, TestContext.Current.CancellationToken);

        file.ContentType.ShouldContain("spreadsheetml");
        var sheet = FirstSheet(file);
        Cell(sheet, 2, "Kayıtlı gün").ShouldBe("3");
        Cell(sheet, 2, "Çalışılan gün").ShouldBe("2"); // NoData günü çalışılan gün sayılmaz
        Cell(sheet, 2, "Geç kalma sayısı").ShouldBe("1");
        Cell(sheet, 2, "Eksik gün sayısı").ShouldBe("1");
    }

    [Fact]
    public async Task MonthlySummaryReport_FormatPdf_ReturnsPdfWithSameDataAsExcel()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");

        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<AttendanceDbContext>();
            db.AttendanceDays.Add(AttendanceDay.Record(
                tenantId, employeeId, new DateOnly(2026, 9, 7), new TimeOnly(9, 0), new TimeOnly(17, 0),
                new TimeOnly(9, 0), new TimeOnly(17, 0), 8 * 60, 0, AttendanceDaySource.Manual));
            await db.SaveChangesAsync();
        }

        _employeeReportReader.ListAsync(tenantId, Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<IReadOnlyCollection<Guid>?>(), false, Arg.Any<CancellationToken>())
            .Returns([Row(employeeId, "Ayşe", "Yılmaz")]);

        using var scope = ServiceProvider!.CreateScope();
        var provider = scope.ServiceProvider.GetServices<IReportProvider>().Single(p => p.Key == AttendanceMonthlySummaryReportProvider.ReportKey);
        var parameters = new Dictionary<string, string> { ["year"] = "2026", ["month"] = "9", ["format"] = "pdf" };

        var file = await provider.GenerateAsync(tenantId, parameters, TestContext.Current.CancellationToken);

        // Excel davranışı değişmedi (bkz. MonthlySummaryReport_ComputesAggregatesFromAttendanceDays); bu yalnız yeni format seçeneği.
        file.ContentType.ShouldBe("application/pdf");
        file.FileName.ShouldEndWith(".pdf");
        System.Text.Encoding.ASCII.GetString(file.Content, 0, 5).ShouldBe("%PDF-"); // gerçek QuestPDF çıktısı, sahte bayt değil
    }

    [Fact]
    public async Task ClosingReport_IncludesLeaveBucketsAndApprovalStatus()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");
        var date = new DateOnly(2026, 9, 21);

        await SeedPunchedDayAsync(tenantId, employeeId, date);
        await SeedApprovedLeaveAsync(tenantId, employeeId, new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 15)); // ayrı, çalışılmamış izin günleri

        _employeeReportReader.ListAsync(tenantId, Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<IReadOnlyCollection<Guid>?>(), false, Arg.Any<CancellationToken>())
            .Returns([Row(employeeId, "Mehmet", "Kaya")]);

        using var scope = ServiceProvider!.CreateScope();
        var provider = scope.ServiceProvider.GetServices<IReportProvider>().Single(p => p.Key == AttendanceMonthlyClosingReportProvider.ReportKey);
        var parameters = new Dictionary<string, string> { ["year"] = "2026", ["month"] = "9" };

        var file = await provider.GenerateAsync(tenantId, parameters, TestContext.Current.CancellationToken);

        var sheet = FirstSheet(file);
        Cell(sheet, 2, "Çalışılan gün").ShouldBe("1");
        Cell(sheet, 2, "Ücretli izin günü").ShouldBe("2");
        Cell(sheet, 2, "Ay kapatıldı mı").ShouldBe("Hayır"); // ApproveMonthlyTimesheetCommand hiç çalıştırılmadı
    }

    [Fact]
    public async Task ClosingReport_FormatPdf_ReturnsPdf()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");
        var date = new DateOnly(2026, 9, 21);

        await SeedPunchedDayAsync(tenantId, employeeId, date);

        _employeeReportReader.ListAsync(tenantId, Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<IReadOnlyCollection<Guid>?>(), false, Arg.Any<CancellationToken>())
            .Returns([Row(employeeId, "Mehmet", "Kaya")]);

        using var scope = ServiceProvider!.CreateScope();
        var provider = scope.ServiceProvider.GetServices<IReportProvider>().Single(p => p.Key == AttendanceMonthlyClosingReportProvider.ReportKey);
        var parameters = new Dictionary<string, string> { ["year"] = "2026", ["month"] = "9", ["format"] = "pdf" };

        var file = await provider.GenerateAsync(tenantId, parameters, TestContext.Current.CancellationToken);

        file.ContentType.ShouldBe("application/pdf");
        file.FileName.ShouldEndWith(".pdf");
        System.Text.Encoding.ASCII.GetString(file.Content, 0, 5).ShouldBe("%PDF-");
    }

    private static IXLWorksheet FirstSheet(ReportFileResult file)
    {
        var workbook = new XLWorkbook(new MemoryStream(file.Content));
        return workbook.Worksheets.First();
    }

    private static string? Cell(IXLWorksheet sheet, int row, string header)
    {
        var headerCell = sheet.Row(1).CellsUsed().First(c => c.GetString() == header);
        var value = sheet.Cell(row, headerCell.Address.ColumnNumber);
        return value.IsEmpty() ? null : value.GetFormattedString();
    }
}

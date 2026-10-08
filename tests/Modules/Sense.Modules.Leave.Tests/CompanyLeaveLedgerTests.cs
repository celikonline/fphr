using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Sense.Modules.Attendance.Infrastructure;
using Sense.Modules.Attendance.Infrastructure.Persistence;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Leave.Application;
using Sense.Modules.Leave.Application.Ledger;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Ledger;
using Sense.Modules.Leave.Domain.Overtime;
using Sense.Modules.Leave.Domain.Profiles;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Modules.Leave.Infrastructure;
using Sense.Modules.Leave.Infrastructure.Persistence;
using Sense.Modules.Leave.Infrastructure.Reporting;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Legislation;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Infrastructure.Export;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Leave.Tests;

/// <summary>
/// senseik-048 "İzinlerim"/"Fazla mesailerim" yanındaki şirket geneli ekran, real PostgreSQL üzerinde: hem interaktif
/// JSON sorgusu (<see cref="GetCompanyLeaveLedgerQuery"/> - handler doğrudan DI'dan çözülür, Dispatcher'ın
/// RequiresPermission ardışığı burada test edilmiyor, LeaveReportProviderTests'teki IReportProvider çözümlemesiyle
/// aynı yaklaşım) hem de Excel çıktısı (<see cref="CompanyLeaveLedgerReportProvider"/>) aynı senaryoyla doğrulanır:
/// bir çalışan sorgulanan ayda onaylı izinli, ikinci bir çalışan yıl içinde 270 saatlik yasal sınırı aşan fazla
/// mesai yapmış.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class CompanyLeaveLedgerTests : TestFixture
{
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();
    private readonly IOrgUnitLookup _orgUnits = Substitute.For<IOrgUnitLookup>();
    private readonly IEmployeeReportReader _employees = Substitute.For<IEmployeeReportReader>();
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    private readonly ITenantTimeZoneLookup _timeZones = Substitute.For<ITenantTimeZoneLookup>();
    private readonly ILeaveDayCalculator _days = Substitute.For<ILeaveDayCalculator>();

    private static readonly OrgUnitTypeInfo CompanyLevel = new(Guid.NewGuid(), "Company", "Şirket", 0);
    private static readonly OrgUnitTypeInfo DepartmentLevel = new(Guid.NewGuid(), "Department", "Departman", 1);
    private static readonly OrgUnitInfo Acme = new(Guid.NewGuid(), CompanyLevel.Id, null, "Acme");
    private static readonly OrgUnitInfo Engineering = new(Guid.NewGuid(), DepartmentLevel.Id, Acme.Id, "Yazılım");

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        services.AddModuleDbContext<LeaveDbContext>(configuration, LeaveDbContext.SchemaName);
        services.AddModuleDbContext<AttendanceDbContext>(configuration, AttendanceDbContext.SchemaName);
        services.AddAttendanceCoreServices(configuration);
        services.AddLeaveServices(configuration);
        // Denetim A5-belge: LeaveRequestRules'un Documents bağı (bu dosyadaki senaryolar belge kuralı dışı).
        services.AddSingleton(DocumentLookupDoubles.Permissive());
        services.AddModuleHandlers(
            LeaveDbContext.SchemaName,
            typeof(LeaveErrors).Assembly,
            typeof(LeavePermissions).Assembly,
            typeof(LeaveDbContext).Assembly,
            typeof(ILeaveRuleSetRepository).Assembly);

        services.AddLocalization(o => o.ResourcesPath = "Resources");
        services.TryAddSingleton<IReportExporter, ExcelReportExporter>();
        services.AddSingleton(_dataScope);
        services.AddSingleton(_orgUnits);
        services.AddSingleton(_employees);
        services.AddSingleton(_permissions);
        services.AddSingleton(_timeZones);
        services.AddSingleton(_days);
        services.AddSingleton<ISimplePdfWriter, QuestPdfSimpleWriter>();
        services.AddSingleton<TimeProvider>(TimeProvider.System);

        _orgUnits.ListActiveTypesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([CompanyLevel, DepartmentLevel]);
        _orgUnits.ListActiveUnitsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([Acme, Engineering]);
        _orgUnits.GetSelfAndDescendantIdsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IReadOnlyCollection<Guid>>().ToList());
    }

    protected override async Task OnDatabaseReadyAsync()
    {
        await ServiceProvider!.GetRequiredService<LeaveDbContext>().Database.MigrateAsync();
        await ServiceProvider.GetRequiredService<AttendanceDbContext>().Database.MigrateAsync();
    }

    private static EmployeeReportRow Row(Guid employeeId, string first, string last) =>
        new(employeeId, first, last, null, new DateOnly(2024, 3, 1), null, "Active", Engineering.Id, null, null, null, "FullTime", null, "Female", null,
            new Dictionary<Guid, Guid> { [DepartmentLevel.Id] = Engineering.Id });

    /// <summary>Onaylı bir yıllık izin kaydı, kural setinin Annual türüyle - hem izin hem bakiye alanlarını beraber test etmek için.</summary>
    private static async Task<Guid> SeedAnnualLeaveAndProfileAsync(LeaveDbContext db, Guid tenantId, Guid employeeId)
    {
        var ruleSet = LeaveRuleSetSeed.CreateTurkishLabourLawRuleSet(tenantId, StatutoryLeaveDurations.Turkey2026);
        db.RuleSets.Add(ruleSet);
        var annual = ruleSet.Types.Single(t => t.Code == LeaveTypeCode.Annual);
        db.Profiles.Add(EmployeeLeaveProfile.Create(tenantId, employeeId, ruleSet.Id, new DateOnly(2024, 3, 1), new DateOnly(2024, 3, 1)));
        db.LedgerEntries.AddRange(
            LeaveLedgerEntry.Accrual(tenantId, employeeId, annual.Id, 14m, new DateOnly(2026, 3, 1)),
            LeaveLedgerEntry.Usage(tenantId, employeeId, annual.Id, 2m, new DateOnly(2026, 5, 4), LeaveLedgerSource.Request, null));
        await db.SaveChangesAsync();
        return annual.Id;
    }

    [Fact]
    public async Task GetCompanyLeaveLedgerHandler_ReportsOnLeave_Balance_AndOvertimeLimit()
    {
        var tenantId = Guid.NewGuid();
        var onLeaveEmployee = Guid.NewGuid();
        var overLimitEmployee = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);

        Guid annualTypeId;
        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<LeaveDbContext>();
            annualTypeId = await SeedAnnualLeaveAndProfileAsync(db, tenantId, onLeaveEmployee);

            // 2026-06-10..2026-06-12 approved leave - inside the queried month (June 2026).
            var leaveTypeId = annualTypeId;
            var approved = LeaveRequest.Submit(tenantId, onLeaveEmployee, leaveTypeId, LeaveTypeCode.Annual, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 12), 3m,
                "Tatil", LeaveRequestSource.SelfService, DateTime.UtcNow, null, null).Value;
            var approvalId = Guid.NewGuid();
            approved.AttachApproval(approvalId);
            approved.Approve(approvalId, DateTime.UtcNow);
            db.Requests.Add(approved);

            // 280 hours of active overtime spread across 2026 (a single record is capped at 24h, OvertimeLimits.MaxDurationMinutes) -
            // above the 270-hour statutory limit (Overtime.AnnualLimitHours seed).
            for (var day = 0; day < 14; day++)
            {
                db.OvertimeRequests.Add(OvertimeRequest.Submit(
                    tenantId, overLimitEmployee, new DateOnly(2026, 1, 1).AddDays(day * 14), new TimeOnly(18, 0), 20 * 60, OvertimeDayKind.Workday, 0, null,
                    OvertimeRequestSource.Manual, DateTime.UtcNow, null, null).Value);
            }

            await db.SaveChangesAsync();
        }

        var visible = new HashSet<Guid> { onLeaveEmployee, overLimitEmployee };
        _dataScope.GetVisibleEmployeeIdsAsync(LeavePermissions.CompanyLedgerRead.Key, Arg.Any<CancellationToken>()).Returns(visible);
        _employees.ListAsync(tenantId, Arg.Is<IReadOnlyCollection<Guid>?>(ids => ids != null && new HashSet<Guid>(ids).SetEquals(visible)),
                Arg.Any<IReadOnlyCollection<Guid>?>(), false, Arg.Any<CancellationToken>())
            .Returns([Row(onLeaveEmployee, "Ayşe", "Yılmaz"), Row(overLimitEmployee, "Mehmet", "Kaya")]);

        using var readScope = ServiceProvider!.CreateScope();
        var handler = readScope.ServiceProvider.GetRequiredService<IQueryHandler<GetCompanyLeaveLedgerQuery, CompanyLeaveLedgerDto>>();
        var result = await handler.Handle(new GetCompanyLeaveLedgerQuery(2026, 6, null, false), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var dto = result.Value;
        dto.OvertimeLimitHours.ShouldBe(270m);

        var onLeaveRow = dto.Rows.Single(r => r.EmployeeId == onLeaveEmployee);
        onLeaveRow.OrgPath.ShouldBe("Acme / Yazılım");
        onLeaveRow.IsOnLeaveInPeriod.ShouldBeTrue();
        onLeaveRow.LeaveStartDate.ShouldBe(new DateOnly(2026, 6, 10));
        onLeaveRow.LeaveEndDate.ShouldBe(new DateOnly(2026, 6, 12));
        onLeaveRow.AnnualLeaveBalance.ShouldBe(12m); // 14 accrued − 2 used
        onLeaveRow.OvertimeLimitExceeded.ShouldBeFalse();

        var overLimitRow = dto.Rows.Single(r => r.EmployeeId == overLimitEmployee);
        overLimitRow.IsOnLeaveInPeriod.ShouldBeFalse();
        overLimitRow.OvertimeHoursYtd.ShouldBe(280m);
        overLimitRow.OvertimeLimitExceeded.ShouldBeTrue();
        overLimitRow.AnnualLeaveBalance.ShouldBeNull(); // no leave profile seeded for this employee
    }

    /// <summary>
    /// HR-only data: both entry points (JSON query via the Dispatcher's RequiresPermission pipeline, Excel via
    /// ReportCommands' RequiredPermission check) must be gated by the same dedicated key, and the employee set must be
    /// narrowed by the caller's data scope for that key (not a broader leave.* read key).
    /// </summary>
    [Fact]
    public void CompanyLedger_EntryPoints_AreGatedByDedicatedPermission()
    {
        var attribute = typeof(GetCompanyLeaveLedgerQuery)
            .GetCustomAttributes(typeof(RequiresPermissionAttribute), inherit: false)
            .Cast<RequiresPermissionAttribute>()
            .Single();
        attribute.Permission.ShouldBe(LeavePermissions.CompanyLedgerRead.Key);

        using var scope = ServiceProvider!.CreateScope();
        var provider = scope.ServiceProvider.GetServices<IReportProvider>().Single(p => p.Key == CompanyLeaveLedgerReportProvider.ReportKey);
        provider.RequiredPermission.ShouldBe(LeavePermissions.CompanyLedgerRead.Key);
        LeavePermissions.All.ShouldContain(LeavePermissions.CompanyLedgerRead);
    }

    [Fact]
    public async Task GetCompanyLeaveLedgerHandler_ExcludesEmployeesOutsideDataScope()
    {
        var tenantId = Guid.NewGuid();
        var visibleEmployee = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);

        _dataScope.GetVisibleEmployeeIdsAsync(LeavePermissions.CompanyLedgerRead.Key, Arg.Any<CancellationToken>())
            .Returns(new HashSet<Guid> { visibleEmployee });
        _employees.ListAsync(tenantId, Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<IReadOnlyCollection<Guid>?>(), false, Arg.Any<CancellationToken>())
            .Returns([Row(visibleEmployee, "Zeynep", "Demir")]);

        using var readScope = ServiceProvider!.CreateScope();
        var handler = readScope.ServiceProvider.GetRequiredService<IQueryHandler<GetCompanyLeaveLedgerQuery, CompanyLeaveLedgerDto>>();
        var result = await handler.Handle(new GetCompanyLeaveLedgerQuery(2026, 6, null, false), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.Select(r => r.EmployeeId).ShouldBe([visibleEmployee]);
        await _employees.Received().ListAsync(
            tenantId,
            Arg.Is<IReadOnlyCollection<Guid>?>(ids => ids != null && ids.Count == 1 && ids.Contains(visibleEmployee)),
            Arg.Any<IReadOnlyCollection<Guid>?>(), false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompanyLeaveLedgerReportProvider_ExportsSameData_WithDynamicOrgColumns()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");

        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<LeaveDbContext>();
            await SeedAnnualLeaveAndProfileAsync(db, tenantId, employeeId);
        }

        _dataScope.GetVisibleEmployeeIdsAsync(LeavePermissions.CompanyLedgerRead.Key, Arg.Any<CancellationToken>())
            .Returns(new HashSet<Guid> { employeeId });
        _employees.ListAsync(tenantId, Arg.Is<IReadOnlyCollection<Guid>?>(ids => ids != null && ids.Contains(employeeId)),
                Arg.Any<IReadOnlyCollection<Guid>?>(), false, Arg.Any<CancellationToken>())
            .Returns([Row(employeeId, "Ayşe", "Yılmaz")]);

        using var scope = ServiceProvider!.CreateScope();
        var provider = scope.ServiceProvider.GetServices<IReportProvider>().Single(p => p.Key == CompanyLeaveLedgerReportProvider.ReportKey);
        var parameters = new Dictionary<string, string> { ["year"] = "2026", ["month"] = "6" };
        var file = await provider.GenerateAsync(tenantId, parameters, TestContext.Current.CancellationToken);

        file.ContentType.ShouldContain("spreadsheetml");
        var workbook = new XLWorkbook(new MemoryStream(file.Content));
        var sheet = workbook.Worksheets.First();
        var headerRow = sheet.Row(1).CellsUsed().Select(c => c.GetString()).ToList();
        headerRow.ShouldContain("Şirket");
        headerRow.ShouldContain("Departman");
        headerRow.ShouldContain("Kalan"); // remainingBalance column reused from LeaveBalanceSummaryReportProvider
        sheet.LastRowUsed()!.RowNumber().ShouldBe(2);
    }
}

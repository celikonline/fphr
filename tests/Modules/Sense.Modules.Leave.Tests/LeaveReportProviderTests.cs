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
using Sense.Modules.Leave.Application;
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
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Infrastructure.Export;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Leave.Tests;

/// <summary>
/// Raporlar menüsü katkıları (roadmap #44/#70) on real PostgreSQL: <c>leave.balance.summary</c> and <c>leave.requests</c>
/// providers read the ledger/requests through LeaveDbContext, take identity/placement from Employee.Contracts and the
/// caller's scope from IDataScopeService - both substituted here. Excel content is read back with ClosedXML.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class LeaveReportProviderTests : TestFixture
{
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();
    private readonly IOrgUnitLookup _orgUnits = Substitute.For<IOrgUnitLookup>();
    private readonly IEmployeeReportReader _employees = Substitute.For<IEmployeeReportReader>();

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
        services.AddModuleHandlers(
            LeaveDbContext.SchemaName,
            typeof(LeaveErrors).Assembly,
            typeof(LeavePermissions).Assembly,
            typeof(LeaveDbContext).Assembly,
            typeof(ILeaveRuleSetRepository).Assembly);

        // Providers render localized Excel headers (SharedResource) through the shared exporter - neither is part of the fixture.
        services.AddLocalization(o => o.ResourcesPath = "Resources");
        services.TryAddSingleton<IReportExporter, ExcelReportExporter>();
        services.AddSingleton(_dataScope);
        services.AddSingleton(_orgUnits);
        services.AddSingleton(_employees);

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

    private static EmployeeReportRow Row(Guid employeeId, string first, string last, DateOnly hireDate) =>
        new(employeeId, first, last, null, hireDate, null, "Active", Engineering.Id, null, null, null, "FullTime", null, "Female", null,
            new Dictionary<Guid, Guid> { [DepartmentLevel.Id] = Engineering.Id });

    private IReportProvider Provider(string key)
    {
        var scope = ServiceProvider!.CreateScope();
        return scope.ServiceProvider.GetServices<IReportProvider>().Single(p => p.Key == key);
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

    [Fact]
    public async Task BalanceSummary_ProjectsAnnualBalance_WithDynamicOrgColumns_AndScopeFilter()
    {
        var tenantId = Guid.NewGuid();
        var inScope = Guid.NewGuid();
        var outOfScope = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        CultureInfo.CurrentUICulture = new CultureInfo("tr-TR"); // headers/enum texts asserted in Turkish

        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var ruleSet = LeaveRuleSetSeed.CreateTurkishLabourLawRuleSet(tenantId, StatutoryLeaveDurations.Turkey2026);
            db.RuleSets.Add(ruleSet);
            var annual = ruleSet.Types.Single(t => t.Code == LeaveTypeCode.Annual);
            foreach (var employeeId in new[] { inScope, outOfScope })
            {
                db.Profiles.Add(EmployeeLeaveProfile.Create(tenantId, employeeId, ruleSet.Id, new DateOnly(2024, 3, 1), new DateOnly(2024, 3, 1)));
                // 2024-03-01..2025-02-28 accrued 14, used 4 → closing 10, rule carries (Limited) → say 5; 2025 period accrued 14, used 2.
                db.LedgerEntries.AddRange(
                    LeaveLedgerEntry.Accrual(tenantId, employeeId, annual.Id, 14m, new DateOnly(2025, 3, 1)),
                    LeaveLedgerEntry.Usage(tenantId, employeeId, annual.Id, 4m, new DateOnly(2025, 4, 7), LeaveLedgerSource.Request, null),
                    LeaveLedgerEntry.CarryOver(tenantId, employeeId, annual.Id, 5m, new DateOnly(2026, 3, 1)),
                    LeaveLedgerEntry.Accrual(tenantId, employeeId, annual.Id, 14m, new DateOnly(2026, 3, 1)),
                    LeaveLedgerEntry.Usage(tenantId, employeeId, annual.Id, 2m, new DateOnly(2026, 5, 4), LeaveLedgerSource.Request, null),
                    LeaveLedgerEntry.Usage(tenantId, employeeId, annual.Id, 3m, new DateOnly(2026, 12, 21), LeaveLedgerSource.Request, null));
            }

            await db.SaveChangesAsync();
        }

        // Caller sees only one of the two employees; the reader honours the visible set it is handed.
        _dataScope.GetVisibleEmployeeIdsAsync(LeavePermissions.BalancesRead.Key, Arg.Any<CancellationToken>())
            .Returns(new HashSet<Guid> { inScope });
        _employees.ListAsync(tenantId, Arg.Is<IReadOnlyCollection<Guid>?>(ids => ids != null && ids.Contains(inScope) && !ids.Contains(outOfScope)),
                Arg.Any<IReadOnlyCollection<Guid>?>(), false, Arg.Any<CancellationToken>())
            .Returns([Row(inScope, "Ayşe", "Yılmaz", new DateOnly(2024, 3, 1))]);

        var file = await Provider(LeaveBalanceSummaryReportProvider.ReportKey).GenerateAsync(tenantId, new Dictionary<string, string>(), TestContext.Current.CancellationToken);

        file.ContentType.ShouldContain("spreadsheetml");
        var sheet = FirstSheet(file);
        sheet.LastRowUsed()!.RowNumber().ShouldBe(2); // header + the one visible employee
        Cell(sheet, 2, "Ad").ShouldBe("Ayşe");
        Cell(sheet, 2, "Şirket").ShouldBe("Acme");
        Cell(sheet, 2, "Departman").ShouldBe("Yazılım");
        Cell(sheet, 2, "Devir kayıpları").ShouldBe("5"); // closing 10, carried 5
        Cell(sheet, 2, "Kullanılan").ShouldBe("6"); // 4 + 2 (the 3-day December leave is future → not "used")
        Cell(sheet, 2, "Kalan").ShouldBe("14"); // 5 + 14 − 2 − 3 future approved
        Cell(sheet, 2, "Güncel dönemdeki ileri tarihli izinler").ShouldBe("3");
    }

    [Fact]
    public async Task LeaveRequests_ListsRequestsIntersectingRange_WithLocalizedStatus()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        CultureInfo.CurrentUICulture = new CultureInfo("tr-TR"); // headers/enum texts asserted in Turkish

        Guid typeId;
        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var ruleSet = LeaveRuleSetSeed.CreateTurkishLabourLawRuleSet(tenantId, StatutoryLeaveDurations.Turkey2026);
            db.RuleSets.Add(ruleSet);
            typeId = ruleSet.Types.Single(t => t.Code == LeaveTypeCode.Annual).Id;

            var inRange = LeaveRequest.Submit(tenantId, employeeId, typeId, LeaveTypeCode.Annual, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 12), 3m, "Tatil",
                LeaveRequestSource.SelfService, DateTime.UtcNow, null, null).Value;
            var approvalId = Guid.NewGuid();
            inRange.AttachApproval(approvalId);
            inRange.Approve(approvalId, DateTime.UtcNow);
            var outOfRange = LeaveRequest.Submit(tenantId, employeeId, typeId, LeaveTypeCode.Annual, new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 4), 2m, null,
                LeaveRequestSource.SelfService, DateTime.UtcNow, null, null).Value;
            db.Requests.AddRange(inRange, outOfRange);
            await db.SaveChangesAsync();
        }

        _dataScope.GetVisibleEmployeeIdsAsync(LeavePermissions.RequestsRead.Key, Arg.Any<CancellationToken>()).Returns((IReadOnlySet<Guid>?)null);
        // Company scope → the provider hands a null visible set through; every same-typed argument needs a matcher for NSubstitute.
        _employees.ListAsync(Arg.Is(tenantId), Arg.Is<IReadOnlyCollection<Guid>?>(ids => ids == null), Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Is(false), Arg.Any<CancellationToken>())
            .Returns([Row(employeeId, "Mehmet", "Kaya", new DateOnly(2020, 1, 1))]);

        var parameters = new Dictionary<string, string> { ["from"] = "2026-06-01", ["to"] = "2026-06-30" };
        var file = await Provider(LeaveRequestsReportProvider.ReportKey).GenerateAsync(tenantId, parameters, TestContext.Current.CancellationToken);

        var sheet = FirstSheet(file);
        sheet.LastRowUsed()!.RowNumber().ShouldBe(2);
        Cell(sheet, 2, "Soyad").ShouldBe("Kaya");
        Cell(sheet, 2, "Gün").ShouldBe("3");
        Cell(sheet, 2, "Durum").ShouldBe("Onaylandı");
        Cell(sheet, 2, "Açıklama").ShouldBe("Tatil");
        file.FileName.ShouldBe("İzin raporu 2026-06-01 2026-06-30.xlsx");
    }

    /// <summary>Kart senseik-030 §1: yalnız Approved talepler "kullanım" sayılır, imza/onay sütunları boş (elle imzalanacak).</summary>
    [Fact]
    public async Task Reconciliation_SummarizesApprovedUsage_WithBlankSignatureColumns()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");

        string annualTypeName;
        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var ruleSet = LeaveRuleSetSeed.CreateTurkishLabourLawRuleSet(tenantId, StatutoryLeaveDurations.Turkey2026);
            db.RuleSets.Add(ruleSet);
            var annual = ruleSet.Types.Single(t => t.Code == LeaveTypeCode.Annual);
            annualTypeName = annual.Name;

            var approved = LeaveRequest.Submit(tenantId, employeeId, annual.Id, LeaveTypeCode.Annual, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 12), 3m, "Tatil",
                LeaveRequestSource.SelfService, DateTime.UtcNow, null, null).Value;
            var approvalId = Guid.NewGuid();
            approved.AttachApproval(approvalId);
            approved.Approve(approvalId, DateTime.UtcNow);
            // Still-pending request in the same period must not count as "used".
            var pending = LeaveRequest.Submit(tenantId, employeeId, annual.Id, LeaveTypeCode.Annual, new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 20), 1m, null,
                LeaveRequestSource.SelfService, DateTime.UtcNow, null, null).Value;
            db.Requests.AddRange(approved, pending);
            await db.SaveChangesAsync();
        }

        _dataScope.GetVisibleEmployeeIdsAsync(LeavePermissions.RequestsRead.Key, Arg.Any<CancellationToken>()).Returns((IReadOnlySet<Guid>?)null);
        _employees.ListAsync(Arg.Is(tenantId), Arg.Is<IReadOnlyCollection<Guid>?>(ids => ids == null), Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Is(false), Arg.Any<CancellationToken>())
            .Returns([Row(employeeId, "Ayşe", "Yılmaz", new DateOnly(2020, 1, 1))]);

        var parameters = new Dictionary<string, string> { ["from"] = "2026-06-01", ["to"] = "2026-06-30" };
        var file = await Provider(LeaveReconciliationReportProvider.ReportKey).GenerateAsync(tenantId, parameters, TestContext.Current.CancellationToken);

        var sheet = FirstSheet(file);
        sheet.LastRowUsed()!.RowNumber().ShouldBe(2);
        Cell(sheet, 2, "Ad").ShouldBe("Ayşe");
        Cell(sheet, 2, "Dönem").ShouldBe("01.06.2026 – 30.06.2026");
        Cell(sheet, 2, "Kullanılan izinler").ShouldBe($"{annualTypeName}: 3");
        Cell(sheet, 2, "Kullanılan").ShouldBe("3"); // pending request'in 1 günü sayılmıyor.
        Cell(sheet, 2, "Çalışan imzası").ShouldBeNull();
        Cell(sheet, 2, "İnsan Kaynakları onayı").ShouldBeNull();
        file.FileName.ShouldBe("İzin mutabakat formu 2026-06-01 2026-06-30.xlsx");
    }

    /// <summary>Kart senseik-030 §2: onaylı fazla mesailerin dönem bazlı dökümü - ücret oranı ve dönüşüm durumu ayrı sütunlarda.</summary>
    [Fact]
    public async Task OvertimeMonthly_ListsApprovedRequests_WithRateAndConversionColumns()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");

        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<LeaveDbContext>();

            // Never converted → still cash-payable, no conversion columns.
            var rate25 = OvertimeRequest.Submit(
                tenantId, employeeId, new DateOnly(2026, 6, 3), new TimeOnly(17, 0), 120, OvertimeDayKind.Workday, 0, "sayım",
                OvertimeRequestSource.Manual, DateTime.UtcNow, null, null, OvertimePayRateKind.Rate25).Value;
            var rate25Approval = Guid.NewGuid();
            rate25.AttachApproval(rate25Approval);
            rate25.Approve(rate25Approval, DateTime.UtcNow).IsSuccess.ShouldBeTrue();
            db.OvertimeRequests.Add(rate25);

            // Explicitly converted to pay (kart senseik-030 §3) → conversion columns filled.
            var rate50 = OvertimeRequest.Submit(
                tenantId, employeeId, new DateOnly(2026, 6, 10), new TimeOnly(18, 0), 120, OvertimeDayKind.Workday, 0, null,
                OvertimeRequestSource.Manual, DateTime.UtcNow, null, null, OvertimePayRateKind.Rate50).Value;
            var rate50Approval = Guid.NewGuid();
            rate50.AttachApproval(rate50Approval);
            rate50.Approve(rate50Approval, DateTime.UtcNow).IsSuccess.ShouldBeTrue();
            var conversion = OvertimeConversion.ToPay(rate50, OvertimeMinutesBasis.Payable, null, DateTime.UtcNow, null).Value;
            rate50.MarkConverted(conversion.Id).IsSuccess.ShouldBeTrue();
            db.OvertimeRequests.Add(rate50);
            db.OvertimeConversions.Add(conversion);

            // Outside the requested month → must not appear.
            var outOfRange = OvertimeRequest.Submit(
                tenantId, employeeId, new DateOnly(2026, 7, 1), new TimeOnly(18, 0), 60, OvertimeDayKind.Workday, 0, null,
                OvertimeRequestSource.Manual, DateTime.UtcNow, null, null).Value;
            var outOfRangeApproval = Guid.NewGuid();
            outOfRange.AttachApproval(outOfRangeApproval);
            outOfRange.Approve(outOfRangeApproval, DateTime.UtcNow).IsSuccess.ShouldBeTrue();
            db.OvertimeRequests.Add(outOfRange);

            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        _dataScope.GetVisibleEmployeeIdsAsync(LeavePermissions.OvertimesRead.Key, Arg.Any<CancellationToken>()).Returns((IReadOnlySet<Guid>?)null);
        _employees.ListAsync(Arg.Is(tenantId), Arg.Is<IReadOnlyCollection<Guid>?>(ids => ids == null), Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Is(false), Arg.Any<CancellationToken>())
            .Returns([Row(employeeId, "Mehmet", "Kaya", new DateOnly(2020, 1, 1))]);

        var parameters = new Dictionary<string, string> { ["year"] = "2026", ["month"] = "6" };
        var file = await Provider(OvertimeMonthlyReportProvider.ReportKey).GenerateAsync(tenantId, parameters, TestContext.Current.CancellationToken);

        var sheet = FirstSheet(file);
        sheet.LastRowUsed()!.RowNumber().ShouldBe(3); // header + 2 June rows; the July request is excluded.
        Cell(sheet, 2, "Ücret oranı").ShouldBe("%25");
        Cell(sheet, 2, "Dönüşüm türü").ShouldBeNull();
        Cell(sheet, 2, "Açıklama").ShouldBe("sayım");
        Cell(sheet, 3, "Ücret oranı").ShouldBe("%50");
        Cell(sheet, 3, "Dönüşüm türü").ShouldBe("Ödeme");
        Cell(sheet, 3, "Dönüşüm durumu").ShouldBe("Kaydedildi");
        file.FileName.ShouldBe("Fazla mesai raporu 2026-06-01 2026-06-30.xlsx");
    }
}

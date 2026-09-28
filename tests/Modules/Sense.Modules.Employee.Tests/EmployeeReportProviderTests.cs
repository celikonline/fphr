using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Employee.Api;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Employee.Infrastructure.Reporting;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Export;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Employee.Tests;

/// <summary>
/// Raporlar menüsü katkıları (Kolay İK "Hazır Şablonlar", 11.3.12): <c>employee.departed</c>, <c>employee.birthdays</c>
/// and <c>employee.turnover</c> providers all read employee rows through the mocked IEmployeeReportReader (real DB
/// employee rows are not needed here - the providers only filter/aggregate what the reader returns) and the caller's
/// scope from IDataScopeService, both substituted. Org unit lookups are left empty so no dynamic career columns appear.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class EmployeeReportProviderTests : TestFixture
{
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();
    private readonly IOrgUnitLookup _orgUnits = Substitute.For<IOrgUnitLookup>();
    private readonly IEmployeeReportReader _employees = Substitute.For<IEmployeeReportReader>();
    private readonly IEmployeeContactLookup _contacts = Substitute.For<IEmployeeContactLookup>();

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        new EmployeeModule().AddModule(services, configuration);

        services.AddLocalization(o => o.ResourcesPath = "Resources");
        services.AddSingleton<IReportExporter, ExcelReportExporter>();
        // GetServices<IReportProvider>() constructs every registered provider (including the sibling
        // EmployeeListReportProvider/SalaryListReportProvider), so their dependencies must resolve too.
        services.AddSingleton<IPermissionService, GrantAllPermissions>();
        services.AddSingleton(_dataScope);
        services.AddSingleton(_orgUnits);
        // Overrides EmployeeModule's own real registrations (last registration wins for single-instance resolution).
        services.AddSingleton(_employees);
        services.AddSingleton(_contacts);

        _orgUnits.ListActiveTypesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
        _orgUnits.ListActiveUnitsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    protected override async Task OnDatabaseReadyAsync()
    {
        await ServiceProvider!.GetRequiredService<EmployeeDbContext>().Database.MigrateAsync();
    }

    private static EmployeeReportRow Row(
        Guid employeeId, string first, string last, string status, DateOnly? hireDate = null, DateOnly? terminationDate = null,
        DateOnly? birthDate = null, Guid? managerId = null) =>
        new(employeeId, first, last, null, hireDate, terminationDate, status, null, managerId, null, null, "FullTime", "Indefinite", "Female", birthDate, new Dictionary<Guid, Guid>());

    private IReportProvider Provider(string key)
    {
        var scope = ServiceProvider!.CreateScope();
        return scope.ServiceProvider.GetServices<IReportProvider>().Single(p => p.Key == key);
    }

    private static IXLWorksheet Sheet(ReportFileResult file, int index = 0)
    {
        var workbook = new XLWorkbook(new MemoryStream(file.Content));
        return workbook.Worksheets.ElementAt(index);
    }

    private static string? Cell(IXLWorksheet sheet, int row, string header)
    {
        var headerCell = sheet.Row(1).CellsUsed().First(c => c.GetString() == header);
        var value = sheet.Cell(row, headerCell.Address.ColumnNumber);
        return value.IsEmpty() ? null : value.GetFormattedString();
    }

    [Fact]
    public async Task Departed_FiltersToTerminatedInYear_AndComputesTenure()
    {
        var tenantId = Guid.NewGuid();
        var manager = Guid.NewGuid();
        var inYear = Guid.NewGuid();
        var outOfYear = Guid.NewGuid();
        var stillActive = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");

        _dataScope.GetVisibleEmployeeIdsAsync(EmployeePermissions.EmployeesReadKey, Arg.Any<CancellationToken>()).Returns((IReadOnlySet<Guid>?)null);
        _employees.ListAsync(tenantId, Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<IReadOnlyCollection<Guid>?>(), true, Arg.Any<CancellationToken>())
            .Returns([
                Row(inYear, "Ayşe", "Yılmaz", nameof(EmployeeStatus.Terminated), new DateOnly(2024, 1, 10), new DateOnly(2026, 3, 20), managerId: manager),
                Row(outOfYear, "Veli", "Kaya", nameof(EmployeeStatus.Terminated), new DateOnly(2020, 1, 1), new DateOnly(2025, 6, 1), managerId: manager),
                Row(stillActive, "Ali", "Demir", nameof(EmployeeStatus.Active), new DateOnly(2022, 5, 1)),
            ]);
        _contacts.GetContactsAsync(tenantId, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(manager)), Arg.Any<CancellationToken>())
            .Returns([new EmployeeContact(manager, "Zeynep", "Arslan", null, null, null, "Active")]);

        var file = await Provider(DepartedEmployeesReportProvider.ReportKey).GenerateAsync(
            tenantId, new Dictionary<string, string> { ["year"] = "2026" }, TestContext.Current.CancellationToken);

        var sheet = Sheet(file);
        sheet.LastRowUsed()!.RowNumber().ShouldBe(2); // header + only the 2026 termination
        Cell(sheet, 2, "Soyad").ShouldBe("Yılmaz");
        Cell(sheet, 2, "Kıdem (gün)").ShouldBe(((new DateOnly(2026, 3, 20)).DayNumber - (new DateOnly(2024, 1, 10)).DayNumber).ToString());
        Cell(sheet, 2, "Yönetici").ShouldBe("Zeynep Arslan");
        file.FileName.ShouldBe("İşten ayrılan çalışanlar 2026.xlsx");
    }

    [Fact]
    public async Task Birthdays_FiltersByMonth_RegardlessOfBirthYear_OrderedByDay()
    {
        var tenantId = Guid.NewGuid();
        var earlyInMonth = Guid.NewGuid();
        var lateInMonth = Guid.NewGuid();
        var otherMonth = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");

        _dataScope.GetVisibleEmployeeIdsAsync(EmployeePermissions.EmployeesReadKey, Arg.Any<CancellationToken>()).Returns((IReadOnlySet<Guid>?)null);
        _employees.ListAsync(tenantId, Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<IReadOnlyCollection<Guid>?>(), false, Arg.Any<CancellationToken>())
            .Returns([
                Row(lateInMonth, "Ayşe", "Yılmaz", nameof(EmployeeStatus.Active), birthDate: new DateOnly(1990, 6, 25)),
                Row(earlyInMonth, "Veli", "Kaya", nameof(EmployeeStatus.Active), birthDate: new DateOnly(1985, 6, 3)),
                Row(otherMonth, "Ali", "Demir", nameof(EmployeeStatus.Active), birthDate: new DateOnly(1992, 7, 15)),
            ]);

        var file = await Provider(EmployeeBirthdayReportProvider.ReportKey).GenerateAsync(
            tenantId, new Dictionary<string, string> { ["month"] = "6" }, TestContext.Current.CancellationToken);

        var sheet = Sheet(file);
        sheet.LastRowUsed()!.RowNumber().ShouldBe(3); // header + the two June birthdays
        Cell(sheet, 2, "Soyad").ShouldBe("Kaya"); // day 3 comes before day 25
        Cell(sheet, 3, "Soyad").ShouldBe("Yılmaz");
    }

    [Fact]
    public async Task Turnover_ComputesRate_AndListsDepartedInYear()
    {
        var tenantId = Guid.NewGuid();
        var departed = Guid.NewGuid();
        var active1 = Guid.NewGuid();
        var active2 = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");

        _dataScope.GetVisibleEmployeeIdsAsync(EmployeePermissions.EmployeesReadKey, Arg.Any<CancellationToken>()).Returns((IReadOnlySet<Guid>?)null);
        _employees.ListAsync(tenantId, Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<IReadOnlyCollection<Guid>?>(), true, Arg.Any<CancellationToken>())
            .Returns([
                Row(departed, "Ayşe", "Yılmaz", nameof(EmployeeStatus.Terminated), new DateOnly(2024, 1, 10), new DateOnly(2026, 3, 20)),
                Row(active1, "Veli", "Kaya", nameof(EmployeeStatus.Active), new DateOnly(2020, 1, 1)),
                Row(active2, "Ali", "Demir", nameof(EmployeeStatus.Active), new DateOnly(2022, 5, 1)),
            ]);
        _contacts.GetContactsAsync(tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([]);

        var file = await Provider(EmployeeTurnoverReportProvider.ReportKey).GenerateAsync(
            tenantId, new Dictionary<string, string> { ["year"] = "2026" }, TestContext.Current.CancellationToken);

        var summary = Sheet(file, 0);
        Cell(summary, 2, "Ayrılan çalışan sayısı").ShouldBe("1");
        Cell(summary, 2, "Aktif çalışan sayısı").ShouldBe("2"); // active1 + active2; the departed row is excluded
        // Numeric cell read as a number: the formatted string follows the host's CurrentCulture ("0,5" on tr-TR).
        var rateHeader = summary.Row(1).CellsUsed().First(c => c.GetString() == "İşten ayrılma oranı");
        summary.Cell(2, rateHeader.Address.ColumnNumber).GetDouble().ShouldBe(0.5);

        var detail = Sheet(file, 1);
        detail.LastRowUsed()!.RowNumber().ShouldBe(2); // header + the one 2026 departure
        Cell(detail, 2, "Soyad").ShouldBe("Yılmaz");
    }
}

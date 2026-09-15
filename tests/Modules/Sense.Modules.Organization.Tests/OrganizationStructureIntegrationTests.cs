using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Employee.Api;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Organization.Api;
using Sense.Modules.Organization.Application;
using Sense.Modules.Organization.Application.Companies;
using Sense.Modules.Organization.Application.HolidayCalendars;
using Sense.Modules.Organization.Application.OrgUnits;
using Sense.Modules.Organization.Application.OrgUnitTypes;
using Sense.Modules.Organization.Domain;
using Sense.Modules.Organization.Domain.HolidayCalendars;
using Sense.Modules.Organization.Domain.OrgUnits;
using Sense.Modules.Organization.Domain.OrgUnitTypes;
using Sense.Modules.Organization.Infrastructure.Persistence;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Context;
using Sense.Shared.Kernel.Results;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Modules.Organization.Tests;

/// <summary>
/// Level management, companies and upcoming holidays end to end through Dispatcher (validation, authorization, unit of work)
/// against real PostgreSQL, with the real Employee module behind IEmployeeLookup. Identity is substituted (permissions granted).
/// </summary>
[Collection("PostgreSQL")]
public sealed class OrganizationStructureIntegrationTests : TestFixture
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        new OrganizationModule().AddModule(services, configuration);
        new EmployeeModule().AddModule(services, configuration);

        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        services.AddSingleton(permissions);
        services.AddSingleton(Substitute.For<IDataScopeService>());
        services.AddSingleton(Substitute.For<IAccessSettingsService>());
    }

    protected override async Task OnDatabaseReadyAsync()
    {
        await ServiceProvider!.GetRequiredService<OrganizationDbContext>().Database.MigrateAsync();
        await ServiceProvider!.GetRequiredService<EmployeeDbContext>().Database.MigrateAsync();
    }

    [Fact]
    public async Task UnitTypes_CreateActivateRenameReorder_KeepActiveLevelsContiguous()
    {
        var tenantId = Guid.NewGuid();
        SetUser(tenantId);
        var types = await SeedTypesAsync(tenantId);

        var custom = await ExecuteCommandAsync(new CreateOrgUnitTypeCommand("Proje Grubu", types[OrgUnitTypeCodes.Department]));
        custom.IsSuccess.ShouldBeTrue();
        custom.Value.ShouldBe(new OrgUnitTypeDto(custom.Value.Id, "Custom_1", "Proje Grubu", 4, true, false, false));

        var activated = await ExecuteCommandAsync(new ActivateOrgUnitTypeCommand(types[OrgUnitTypeCodes.Project], types[OrgUnitTypeCodes.Company]));
        activated.Value.SortOrder.ShouldBe(2);
        activated.Value.IsActive.ShouldBeTrue();
        (await ExecuteCommandAsync(new ActivateOrgUnitTypeCommand(types[OrgUnitTypeCodes.Branch], null))).Error.Code.ShouldBe(OrganizationDomainErrors.OrgUnitTypeAlreadyActive);

        (await ExecuteCommandAsync(new RenameOrgUnitTypeCommand(types[OrgUnitTypeCodes.Title], "Görev Unvanı"))).Value.Name.ShouldBe("Görev Unvanı");
        await Should.ThrowAsync<ValidationException>(() => ExecuteCommandAsync(new RenameOrgUnitTypeCommand(types[OrgUnitTypeCodes.Title], "")));
        await Should.ThrowAsync<ValidationException>(() => ExecuteCommandAsync(new CreateOrgUnitTypeCommand(new string('x', OrganizationLimits.NameMaxLength + 1), null)));

        var active = (await ExecuteQueryAsync(new ListOrgUnitTypesQuery(ActiveOnly: true))).Value;
        active.Select(t => t.Code).ShouldBe(["Company", "Project", "Branch", "Department", "Custom_1", "Title"]);
        active.Select(t => t.SortOrder).ShouldBe([1, 2, 3, 4, 5, 6]);

        var ids = active.Select(t => t.Id).ToList();
        (await ExecuteCommandAsync(new ReorderOrgUnitTypesCommand([ids[1], ids[0], .. ids[2..]]))).Error.Code.ShouldBe(OrganizationDomainErrors.OrgUnitTypeCompanyMustBeFirst);
        (await ExecuteCommandAsync(new ReorderOrgUnitTypesCommand(ids[..^1]))).Error.Code.ShouldBe(OrganizationDomainErrors.OrgUnitTypeOrderInvalid);

        var reordered = await ExecuteCommandAsync(new ReorderOrgUnitTypesCommand([ids[0], ids[5], ids[4], ids[3], ids[2], ids[1]]));
        reordered.IsSuccess.ShouldBeTrue();
        reordered.Value.Select(t => $"{t.Code}:{t.SortOrder}").ShouldBe(["Company:1", "Title:2", "Custom_1:3", "Department:4", "Branch:5", "Project:6", "Team:7", "ApprovalUnit:8"]);

        var persisted = await ServiceProvider!.GetRequiredService<OrganizationDbContext>().OrgUnitTypes.AsNoTracking().OrderBy(t => t.SortOrder).ToListAsync();
        persisted.Select(t => t.Code).ShouldBe(["Company", "Title", "Custom_1", "Department", "Branch", "Project", "Team", "ApprovalUnit"]);

        (await ExecuteCommandAsync(new CreateOrgUnitTypeCommand("Seviye 9", null))).IsSuccess.ShouldBeTrue();
        (await ExecuteCommandAsync(new CreateOrgUnitTypeCommand("Seviye 10", null))).Value.Code.ShouldBe("Custom_3");
        var capped = await ExecuteCommandAsync(new CreateOrgUnitTypeCommand("Seviye 11", null));
        capped.Error.Code.ShouldBe(OrganizationDomainErrors.OrgUnitTypeLimitReached);
    }

    [Fact]
    public async Task DeleteUnitType_ProtectsCompany_ConflictsWhileInUse_ThenDeactivatesLevelAndUnits()
    {
        var tenantId = Guid.NewGuid();
        SetUser(tenantId);
        var types = await SeedTypesAsync(tenantId);
        var db = ServiceProvider!.GetRequiredService<OrganizationDbContext>();

        var root = OrgUnit.Create(tenantId, types[OrgUnitTypeCodes.Company], null, "Acme").Value;
        var branch = OrgUnit.Create(tenantId, types[OrgUnitTypeCodes.Branch], root.Id, "İstanbul").Value;
        var department = OrgUnit.Create(tenantId, types[OrgUnitTypeCodes.Department], branch.Id, "Finans").Value;
        db.OrgUnits.AddRange(root, branch, department);
        await db.SaveChangesAsync();

        var employee = await AddEmployeeAsync(tenantId, branch.Id);

        var company = await ExecuteCommandAsync(new DeleteOrgUnitTypeCommand(types[OrgUnitTypeCodes.Company]));
        company.Error.Code.ShouldBe(OrganizationDomainErrors.OrgUnitTypeSystemImmutable);
        company.Error.Type.ShouldBe(ErrorType.Rule);

        var inUse = await ExecuteCommandAsync(new DeleteOrgUnitTypeCommand(types[OrgUnitTypeCodes.Branch]));
        inUse.Error.Code.ShouldBe(OrganizationErrors.UnitTypeInUse);
        inUse.Error.Type.ShouldBe(ErrorType.Conflict);

        await SetEmployeeStatusAsync(employee, EmployeeStatus.Inactive);
        (await ExecuteCommandAsync(new DeleteOrgUnitTypeCommand(types[OrgUnitTypeCodes.Branch]))).IsSuccess.ShouldBeTrue();

        var levels = (await ExecuteQueryAsync(new ListOrgUnitTypesQuery())).Value;
        levels.Select(t => $"{t.Code}:{t.SortOrder}:{t.IsActive}").ShouldBe(
            ["Company:1:True", "Department:2:True", "Title:3:True", "Branch:4:False", "Team:5:False", "Project:6:False", "ApprovalUnit:7:False"]);

        var units = await db.OrgUnits.AsNoTracking().ToDictionaryAsync(u => u.Id);
        units[branch.Id].IsActive.ShouldBeFalse();
        units[department.Id].IsActive.ShouldBeTrue();
        units[department.Id].ParentId.ShouldBe(root.Id);

        var tree = (await ExecuteQueryAsync(new GetOrgUnitTreeQuery())).Value;
        tree.ShouldHaveSingleItem().Children.ShouldHaveSingleItem().Id.ShouldBe(department.Id);

        (await ExecuteCommandAsync(new DeleteOrgUnitTypeCommand(types[OrgUnitTypeCodes.Branch]))).Error.Code.ShouldBe(OrganizationDomainErrors.OrgUnitTypeAlreadyInactive);
        (await ExecuteCommandAsync(new DeleteOrgUnitTypeCommand(Guid.NewGuid()))).Error.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task Companies_CreateWithCalendar_ListAssign_AndCompanyTypeUnitCreatesCompanyRow()
    {
        var tenantId = Guid.NewGuid();
        SetUser(tenantId);
        var types = await SeedTypesAsync(tenantId);
        var trCalendar = await AddCalendarAsync(tenantId, "Türk resmi tatil takvimi", isSystem: true);
        var customCalendar = await AddCalendarAsync(tenantId, "Şirket günleri", isSystem: false);

        var created = await ExecuteCommandAsync(new CreateCompanyCommand("Kolay Holding", trCalendar.Id));
        created.IsSuccess.ShouldBeTrue();
        created.Value.Name.ShouldBe("Kolay Holding");
        created.Value.HolidayCalendarId.ShouldBe(trCalendar.Id);
        created.Value.HolidayCalendarName.ShouldBe("Türk resmi tatil takvimi");

        var db = ServiceProvider!.GetRequiredService<OrganizationDbContext>();
        var companyUnit = await db.OrgUnits.AsNoTracking().SingleAsync(u => u.Id == created.Value.OrgUnitId);
        companyUnit.TypeId.ShouldBe(types[OrgUnitTypeCodes.Company]);
        companyUnit.ParentId.ShouldBeNull();

        (await ExecuteCommandAsync(new CreateCompanyCommand("Hayalet", Guid.NewGuid()))).Error.Code.ShouldBe(OrganizationErrors.HolidayCalendarNotFound);
        await Should.ThrowAsync<ValidationException>(() => ExecuteCommandAsync(new CreateCompanyCommand(" ", null)));

        var viaUnits = await ExecuteCommandAsync(new CreateOrgUnitCommand(types[OrgUnitTypeCodes.Company], null, "Acme Lojistik", null, null));
        viaUnits.IsSuccess.ShouldBeTrue();
        var department = await ExecuteCommandAsync(new CreateOrgUnitCommand(types[OrgUnitTypeCodes.Department], viaUnits.Value.Id, "Depo", null, null));
        department.IsSuccess.ShouldBeTrue();
        (await db.Companies.AsNoTracking().CountAsync(c => c.OrgUnitId == viaUnits.Value.Id)).ShouldBe(1);
        (await db.Companies.AsNoTracking().AnyAsync(c => c.OrgUnitId == department.Value.Id)).ShouldBeFalse();

        var list = (await ExecuteQueryAsync(new ListCompaniesQuery())).Value;
        list.Select(c => c.Name).ShouldBe(["Acme Lojistik", "Kolay Holding"]);
        var logistics = list[0];
        logistics.HolidayCalendarId.ShouldBeNull();

        var assigned = await ExecuteCommandAsync(new AssignCompanyHolidayCalendarCommand(logistics.Id, customCalendar.Id));
        assigned.Value.ShouldBe(new CompanyDto(logistics.Id, viaUnits.Value.Id, "Acme Lojistik", customCalendar.Id, "Şirket günleri", null));
    }

    [Fact]
    public async Task UpcomingHolidays_ResolveCompanyEmployeeCompanyAndDefault_AndProjectRecurringEntries()
    {
        var tenantId = Guid.NewGuid();
        SetUser(tenantId);
        var types = await SeedTypesAsync(tenantId);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var trCalendar = await AddCalendarAsync(tenantId, "Türk resmi tatil takvimi", isSystem: true);
        var inTenDays = today.AddDays(10) is { Month: 2, Day: 29 } ? today.AddDays(11) : today.AddDays(10);
        var fiveDaysAgo = today.AddDays(-5) is { Month: 2, Day: 29 } ? today.AddDays(-6) : today.AddDays(-5);
        await AddEntryAsync(tenantId, trCalendar.Id, new DateOnly(2000, inTenDays.Month, inTenDays.Day), "Yakın bayram", recurring: true);
        await AddEntryAsync(tenantId, trCalendar.Id, new DateOnly(2000, fiveDaysAgo.Month, fiveDaysAgo.Day), "Geçen bayram", recurring: true);
        await AddEntryAsync(tenantId, trCalendar.Id, today.AddDays(200), "Uzak bayram", recurring: false);

        var otherCalendar = await AddCalendarAsync(tenantId, "Almanya", isSystem: false);
        await AddEntryAsync(tenantId, otherCalendar.Id, today.AddDays(3), "Tag der Einheit", recurring: false);

        // Before any company has a calendar the tenant's system TR calendar is the default.
        var fallback = (await ExecuteQueryAsync(new GetUpcomingHolidaysQuery())).Value;
        fallback.ShouldBe([new UpcomingHolidayDto(inTenDays, "Yakın bayram", trCalendar.Id, "Türk resmi tatil takvimi")]);

        var holding = (await ExecuteCommandAsync(new CreateCompanyCommand("Holding", trCalendar.Id))).Value;
        var germany = (await ExecuteCommandAsync(new CreateCompanyCommand("GmbH", otherCalendar.Id))).Value;
        var berlin = (await ExecuteCommandAsync(new CreateOrgUnitCommand(types[OrgUnitTypeCodes.Department], germany.OrgUnitId, "Berlin", null, null))).Value;
        var team = (await ExecuteCommandAsync(new CreateOrgUnitCommand(types[OrgUnitTypeCodes.Title], berlin.Id, "Entwickler", null, null))).Value;

        var yearAhead = (await ExecuteQueryAsync(new GetUpcomingHolidaysQuery(366, holding.Id))).Value;
        yearAhead.Select(h => h.Name).ShouldBe(["Yakın bayram", "Uzak bayram", "Geçen bayram"]);
        yearAhead[2].Date.ShouldBe(new DateOnly(fiveDaysAgo.Year + 1, fiveDaysAgo.Month, fiveDaysAgo.Day));
        yearAhead.ShouldAllBe(h => h.HolidayCalendarId == trCalendar.Id && h.CalendarName == "Türk resmi tatil takvimi");

        (await ExecuteQueryAsync(new GetUpcomingHolidaysQuery(90, germany.Id))).Value.Select(h => h.Name).ShouldBe(["Tag der Einheit"]);

        var employee = await AddEmployeeAsync(tenantId, team.Id);
        SetUser(tenantId, employee.Id);
        var mine = (await ExecuteQueryAsync(new GetUpcomingHolidaysQuery())).Value;
        mine.ShouldHaveSingleItem().ShouldBe(new UpcomingHolidayDto(today.AddDays(3), "Tag der Einheit", otherCalendar.Id, "Almanya"));

        // An explicit company wins over the caller's own company.
        (await ExecuteQueryAsync(new GetUpcomingHolidaysQuery(90, holding.Id))).Value.ShouldHaveSingleItem().Name.ShouldBe("Yakın bayram");

        // A company without a calendar falls back to the tenant default (the earliest company with one).
        await ExecuteCommandAsync(new AssignCompanyHolidayCalendarCommand(germany.Id, null));
        (await ExecuteQueryAsync(new GetUpcomingHolidaysQuery())).Value.ShouldHaveSingleItem().HolidayCalendarId.ShouldBe(trCalendar.Id);

        (await ExecuteQueryAsync(new GetUpcomingHolidaysQuery(90, Guid.NewGuid()))).Error.Code.ShouldBe(OrganizationErrors.CompanyNotFound);
        await Should.ThrowAsync<ValidationException>(() => ExecuteQueryAsync(new GetUpcomingHolidaysQuery(400)));
    }

    private async Task<Dictionary<string, Guid>> SeedTypesAsync(Guid tenantId)
    {
        var db = ServiceProvider!.GetRequiredService<OrganizationDbContext>();
        var types = OrgUnitTypeCatalog.CreateAll(tenantId).Select(r => r.Value).ToList();
        db.OrgUnitTypes.AddRange(types);
        await db.SaveChangesAsync();
        return types.ToDictionary(t => t.Code, t => t.Id);
    }

    private async Task<HolidayCalendar> AddCalendarAsync(Guid tenantId, string name, bool isSystem)
    {
        var db = ServiceProvider!.GetRequiredService<OrganizationDbContext>();
        var calendar = HolidayCalendar.Create(tenantId, name, isSystem ? "TR" : "DE", isSystem).Value;
        db.HolidayCalendars.Add(calendar);
        await db.SaveChangesAsync();
        return calendar;
    }

    private async Task AddEntryAsync(Guid tenantId, Guid calendarId, DateOnly date, string name, bool recurring)
    {
        var db = ServiceProvider!.GetRequiredService<OrganizationDbContext>();
        db.HolidayCalendarEntries.Add(HolidayCalendarEntry.Create(tenantId, calendarId, date, name, recurring).Value);
        await db.SaveChangesAsync();
    }

    private async Task<EmployeeEntity> AddEmployeeAsync(Guid tenantId, Guid orgUnitId)
    {
        var db = ServiceProvider!.GetRequiredService<EmployeeDbContext>();
        var employee = EmployeeEntity.Create(tenantId, new EmployeeInfo("Ada", "Çalışan"), userId: null, orgUnitId: orgUnitId).Value;
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return employee;
    }

    private async Task SetEmployeeStatusAsync(EmployeeEntity employee, EmployeeStatus status)
    {
        employee.SetStatus(status);
        await ServiceProvider!.GetRequiredService<EmployeeDbContext>().SaveChangesAsync();
    }

    private void SetUser(Guid tenantId, Guid? employeeId = null)
    {
        ((CurrentUserAccessor)CurrentUser!).Set(new AuthenticatedUser(
            UserId: Guid.NewGuid(), EmployeeId: employeeId, Email: "hr@test.local", DisplayName: "HR",
            Roles: new HashSet<string>(StringComparer.Ordinal), IsPlatformAdmin: false,
            ImpersonatorUserId: null, ApiKeyId: null, CorrelationId: null, IpAddress: "127.0.0.1"));
        TenantContextSetter!.BeginScope(tenantId);
    }
}

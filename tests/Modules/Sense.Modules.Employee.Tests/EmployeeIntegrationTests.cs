using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Employee.Api;
using Sense.Modules.Employee.Application;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Organization.Api;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Organization.Domain.OrgUnits;
using Sense.Modules.Organization.Domain.OrgUnitTypes;
using Sense.Modules.Organization.Infrastructure.Persistence;
using Sense.Shared.Contracts.Bulk;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Context;
using Sense.Shared.Kernel.Results;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Employee.Tests;

/// <summary>
/// Employee use cases end to end through Dispatcher (validation, authorization, unit of work) against real PostgreSQL,
/// with the real Organization module for org unit lookups. Identity is replaced by test doubles (permissions granted,
/// data scope set per test, account lookup in memory).
/// </summary>
[Collection("PostgreSQL")]
public sealed class EmployeeIntegrationTests : TestFixture
{
    private readonly FakeDataScope _scope = new();
    private readonly FakeUserAccountLookup _accounts = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        new OrganizationModule().AddModule(services, configuration);
        new EmployeeModule().AddModule(services, configuration);
        services.AddSingleton<IPermissionService, GrantAllPermissions>();
        services.AddSingleton<IDataScopeService>(_scope);
        services.AddSingleton<IAccessSettingsService, AllAccessSettingsEnabled>();
        services.AddSingleton<IUserAccountLookup>(_accounts);
    }

    protected override async Task OnDatabaseReadyAsync()
    {
        await ServiceProvider!.GetRequiredService<OrganizationDbContext>().Database.MigrateAsync();
        await ServiceProvider!.GetRequiredService<EmployeeDbContext>().Database.MigrateAsync();
    }

    [Fact]
    public async Task CreateAndUpdate_ValidateReferencesUniquenessAndHierarchy()
    {
        var tenantId = Guid.NewGuid();
        SetUser(tenantId, platformAdmin: true);
        var units = await SeedOrgUnitsAsync(tenantId);

        var boss = await CreateAsync("Ahmet", "Yılmaz", orgUnitId: units.Root);
        var nationalId = Tckn.Next();
        var created = await ExecuteCommandAsync(new CreateEmployeeCommand(
            "Ayşe", "Demir", nationalId, "Ayse.Demir@acme.com", "ayse@example.com", "+90 555 000 00 00",
            new DateOnly(2021, 3, 1), new DateOnly(1990, 4, 2), units.Sales, boss.Id));

        created.IsSuccess.ShouldBeTrue();
        var ayse = created.Value;
        ayse.DisplayName.ShouldBe("Ayşe Demir");
        ayse.WorkEmail.ShouldBe("ayse.demir@acme.com");
        ayse.ManagerDisplayName.ShouldBe("Ahmet Yılmaz");
        ayse.AccountStatus.ShouldBe(UserAccountStatus.None);
        ayse.Status.ShouldBe(nameof(EmployeeStatus.Active));

        ShouldFail(await ExecuteCommandAsync(NewCreate("Veli", "Kaya", nationalId: nationalId)), EmployeeErrors.NationalIdTaken);
        ShouldFail(await ExecuteCommandAsync(NewCreate("Veli", "Kaya", orgUnitId: Guid.NewGuid())), EmployeeErrors.OrgUnitNotFound);
        ShouldFail(await ExecuteCommandAsync(NewCreate("Veli", "Kaya", managerId: Guid.NewGuid())), EmployeeErrors.ManagerNotFound);
        await Should.ThrowAsync<ValidationException>(() => ExecuteCommandAsync(NewCreate("Veli", "Kaya", nationalId: "12345678901")));
        await Should.ThrowAsync<ValidationException>(() => ExecuteCommandAsync(NewCreate("", "Kaya")));
        await Should.ThrowAsync<ValidationException>(() => ExecuteCommandAsync(NewCreate("Veli", "Kaya", workEmail: "nope")));

        // Keeping one's own national id is not a conflict; names recompute the display name.
        var updated = await ExecuteCommandAsync(new UpdateEmployeeCommand(
            ayse.Id, "Ayşe Nur", "Demir", nationalId, ayse.WorkEmail, null, null, ayse.HireDate, ayse.BirthDate, units.Emea, boss.Id));
        updated.IsSuccess.ShouldBeTrue();
        updated.Value.DisplayName.ShouldBe("Ayşe Nur Demir");
        updated.Value.OrgUnitId.ShouldBe(units.Emea);
        updated.Value.PersonalEmail.ShouldBeNull();

        ShouldFail(await ExecuteCommandAsync(UpdateOf(ayse.Id, "Ayşe", "Demir", managerId: ayse.Id)), EmployeeDomainErrors.ManagerSelf);
        ShouldFail(await ExecuteCommandAsync(UpdateOf(boss.Id, "Ahmet", "Yılmaz", managerId: ayse.Id)), EmployeeDomainErrors.ManagerCycle);

        var detail = await ExecuteQueryAsync(new GetEmployeeQuery(ayse.Id));
        detail.Value.DisplayName.ShouldBe("Ayşe Nur Demir");
        detail.Value.NationalId.ShouldBe(nationalId);
        detail.Value.ManagerEmployeeId.ShouldBe(boss.Id);

        (await ExecuteQueryAsync(new GetEmployeeQuery(Guid.NewGuid()))).Error.Code.ShouldBe(EmployeeErrors.EmployeeNotFound);
    }

    [Fact]
    public async Task List_AppliesFiltersSortAccountStatusAndDataScope()
    {
        var tenantId = Guid.NewGuid();
        SetUser(tenantId, platformAdmin: true);
        var units = await SeedOrgUnitsAsync(tenantId);

        var boss = await CreateAsync("Zeynep", "Arslan", orgUnitId: units.Root);
        var aliNationalId = Tckn.Next();
        var ali = await CreateAsync("Ali", "Kaya", orgUnitId: units.Sales, managerId: boss.Id, nationalId: aliNationalId, workEmail: "ali.kaya@acme.com");
        var berk = await CreateAsync("Berk", "Çelik", orgUnitId: units.Emea, managerId: boss.Id);
        var cem = await CreateAsync("Cem", "Aydın");
        await SetStatusAsync(berk.Id, EmployeeStatus.Terminated);

        var userId = Guid.NewGuid();
        await ServiceProvider!.GetRequiredService<IEmployeeUserLinker>().LinkUserAsync(tenantId, boss.Id, userId);
        _accounts.Accounts[userId] = new UserAccountInfo(userId, UserAccountStatus.Active, "Owner", null);

        (await ListAsync()).Select(e => e.DisplayName).ShouldBe(["Ali Kaya", "Berk Çelik", "Cem Aydın", "Zeynep Arslan"]);
        (await ListAsync(sort: "-firstName")).Select(e => e.FirstName).ShouldBe(["Zeynep", "Cem", "Berk", "Ali"]);
        (await ListAsync(filter: Filter(orgUnitId: units.Sales))).Select(e => e.Id).ShouldBe([ali.Id]);
        (await ListAsync(filter: Filter(orgUnitId: units.Sales, includeSubUnits: true))).Select(e => e.Id).ShouldBe([ali.Id, berk.Id], ignoreOrder: true);
        (await ListAsync(filter: Filter(status: "terminated"))).Select(e => e.Id).ShouldBe([berk.Id]);
        (await ListAsync(filter: Filter(hasUserAccount: false))).Count.ShouldBe(3);
        (await ListAsync(filter: Filter(managerId: boss.Id))).Select(e => e.Id).ShouldBe([ali.Id, berk.Id], ignoreOrder: true);
        (await ListAsync(q: "ALI.KAYA@")).Select(e => e.Id).ShouldBe([ali.Id]);
        (await ListAsync(q: aliNationalId)).Select(e => e.Id).ShouldBe([ali.Id]);

        var linked = (await ListAsync(filter: Filter(hasUserAccount: true))).ShouldHaveSingleItem();
        linked.Id.ShouldBe(boss.Id);
        linked.AccountStatus.ShouldBe(UserAccountStatus.Active);

        var aliRow = (await ListAsync(q: "Ali")).Single(e => e.Id == ali.Id);
        aliRow.NationalIdMasked.ShouldBe("*******" + aliNationalId[^4..]);
        aliRow.ManagerDisplayName.ShouldBe("Zeynep Arslan");

        var bossDetail = await ExecuteQueryAsync(new GetEmployeeQuery(boss.Id));
        bossDetail.Value.AccountStatus.ShouldBe(UserAccountStatus.Active);
        bossDetail.Value.AccessType.ShouldBe("Owner");

        await Should.ThrowAsync<ValidationException>(() => ExecuteQueryAsync(new ListEmployeesQuery(new PagedQuery(), Filter(status: "Retired"))));

        // Restricted caller: only the visible set is listed; other employees are 404 on detail/update.
        SetUser(tenantId, platformAdmin: false);
        _scope.Visible = new HashSet<Guid> { ali.Id, cem.Id };
        (await ListAsync()).Select(e => e.Id).ShouldBe([ali.Id, cem.Id]);
        (await ExecuteQueryAsync(new GetEmployeeQuery(ali.Id))).IsSuccess.ShouldBeTrue();
        (await ExecuteQueryAsync(new GetEmployeeQuery(boss.Id))).Error.Code.ShouldBe(ErrorCodes.OutOfScope);
        (await ExecuteCommandAsync(UpdateOf(boss.Id, "Zeynep", "Arslan"))).Error.Code.ShouldBe(ErrorCodes.OutOfScope);
    }

    [Fact]
    public async Task BulkAssignManager_ReportsPerItemOutcome_ForExplicitAndFilterSelections()
    {
        var tenantId = Guid.NewGuid();
        SetUser(tenantId, platformAdmin: true);
        var units = await SeedOrgUnitsAsync(tenantId);

        var x = await CreateAsync("Xavier", "Top", orgUnitId: units.Root);
        var manager = await CreateAsync("Mert", "Yönetici", orgUnitId: units.Root, managerId: x.Id);
        var y = await CreateAsync("Yasemin", "Satış", orgUnitId: units.Sales);
        var z = await CreateAsync("Zafer", "Satış", orgUnitId: units.Sales);
        var unknown = Guid.NewGuid();

        var explicitResult = await ExecuteCommandAsync(new BulkAssignManagerCommand(
            new EmployeeSelection([manager.Id, x.Id, y.Id, unknown], null, null), manager.Id));

        explicitResult.IsSuccess.ShouldBeTrue();
        var r = explicitResult.Value;
        (r.Requested, r.Succeeded, r.Skipped, r.Failed).ShouldBe((4, 1, 1, 2));
        r.Items.Single(i => i.EmployeeId == manager.Id).ShouldBe(new BulkItemResult(manager.Id, BulkItemOutcome.Skipped, EmployeeDomainErrors.ManagerSelf));
        r.Items.Single(i => i.EmployeeId == x.Id).ShouldBe(new BulkItemResult(x.Id, BulkItemOutcome.Failed, EmployeeDomainErrors.ManagerCycle));
        r.Items.Single(i => i.EmployeeId == y.Id).Outcome.ShouldBe(BulkItemOutcome.Succeeded);
        r.Items.Single(i => i.EmployeeId == unknown).ShouldBe(new BulkItemResult(unknown, BulkItemOutcome.Failed, EmployeeErrors.EmployeeNotFound));

        // "Tümünü seç" over the Sales filter minus an exclusion.
        await ExecuteCommandAsync(UpdateOf(y.Id, "Yasemin", "Satış", orgUnitId: units.Sales));
        var filterResult = await ExecuteCommandAsync(new BulkAssignManagerCommand(
            new EmployeeSelection(null, Filter(orgUnitId: units.Sales), [y.Id]), manager.Id));
        filterResult.Value.Items.ShouldHaveSingleItem().ShouldBe(new BulkItemResult(z.Id, BulkItemOutcome.Succeeded, null));

        (await ExecuteQueryAsync(new GetEmployeeQuery(z.Id))).Value.ManagerEmployeeId.ShouldBe(manager.Id);
        (await ExecuteQueryAsync(new GetEmployeeQuery(y.Id))).Value.ManagerEmployeeId.ShouldBeNull();

        // Out-of-scope explicit ids are reported, not silently changed.
        SetUser(tenantId, platformAdmin: false);
        _scope.Visible = new HashSet<Guid> { z.Id };
        var scoped = await ExecuteCommandAsync(new BulkAssignManagerCommand(new EmployeeSelection([y.Id], null, null), manager.Id));
        scoped.Value.Items.ShouldHaveSingleItem().Outcome.ShouldBe(BulkItemOutcome.Failed);

        ShouldFail(await ExecuteCommandAsync(new BulkAssignManagerCommand(new EmployeeSelection([z.Id], null, null), Guid.NewGuid())), EmployeeErrors.ManagerNotFound);
    }

    [Fact]
    public async Task OrgChartDirectoryAndBirthdays_AreCompanyWideAndActiveOnly()
    {
        var tenantId = Guid.NewGuid();
        SetUser(tenantId, platformAdmin: true);
        var units = await SeedOrgUnitsAsync(tenantId);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var ceo = await CreateAsync("Ceyda", "Başkan", orgUnitId: units.Root, birthDate: today.AddYears(-40).AddDays(5));
        var cto = await CreateAsync("Tolga", "Teknik", orgUnitId: units.Sales, managerId: ceo.Id, workPhone: "+90 212 000 00 01");
        var dev = await CreateAsync("Deniz", "Geliştirici", orgUnitId: units.Emea, managerId: cto.Id, birthDate: today.AddYears(-25).AddDays(60));
        var gone = await CreateAsync("Eski", "Çalışan", managerId: ceo.Id, birthDate: today.AddYears(-30).AddDays(1));
        await SetStatusAsync(gone.Id, EmployeeStatus.Terminated);

        // Self-scoped caller (e.g. Employee role) still sees the whole company in these views.
        SetUser(tenantId, platformAdmin: false);
        _scope.Visible = new HashSet<Guid>();

        var chart = (await ExecuteQueryAsync(new GetOrgChartQuery())).Value;
        var root = chart.ShouldHaveSingleItem();
        root.Id.ShouldBe(ceo.Id);
        root.Children.ShouldHaveSingleItem().Id.ShouldBe(cto.Id);
        root.Children[0].Children.ShouldHaveSingleItem().Id.ShouldBe(dev.Id);

        var directory = (await ExecuteQueryAsync(new GetEmployeeDirectoryQuery(new PagedQuery { PageSize = 2 }, null))).Value;
        directory.TotalCount.ShouldBe(3);
        directory.Items.Select(d => d.DisplayName).ShouldBe(["Ceyda Başkan", "Deniz Geliştirici"]);
        var byUnit = (await ExecuteQueryAsync(new GetEmployeeDirectoryQuery(new PagedQuery { Q = "tolga" }, units.Sales))).Value;
        byUnit.Items.ShouldHaveSingleItem().ShouldBe(new DirectoryEntryDto(cto.Id, "Tolga Teknik", units.Sales, null, "+90 212 000 00 01"));

        var within30 = (await ExecuteQueryAsync(new GetUpcomingBirthdaysQuery())).Value;
        within30.ShouldHaveSingleItem().ShouldBe(new UpcomingBirthdayDto(ceo.Id, "Ceyda Başkan", units.Root, BirthdayCalculator.NextBirthday(ceo.BirthDate!.Value, today)));
        (await ExecuteQueryAsync(new GetUpcomingBirthdaysQuery(90))).Value.Select(b => b.Id).ShouldBe([ceo.Id, dev.Id]);
    }

    [Fact]
    public async Task DepartmentDistribution_GroupsActiveEmployeesByDepartmentLevelAncestorAndExcludesTerminated()
    {
        var tenantId = Guid.NewGuid();
        SetUser(tenantId, platformAdmin: true);
        var units = await SeedOrgUnitsAsync(tenantId);

        // Root is Company-level (no Department ancestor): falls in the "unassigned" bucket. Sales/Emea are both
        // Department-level (Emea nested under Sales), so each counts under its own unit, not its parent's.
        var ceo = await CreateAsync("Ceyda", "Başkan", orgUnitId: units.Root);
        var cto = await CreateAsync("Tolga", "Teknik", orgUnitId: units.Sales, managerId: ceo.Id);
        var dev = await CreateAsync("Deniz", "Geliştirici", orgUnitId: units.Emea, managerId: cto.Id);
        var gone = await CreateAsync("Eski", "Çalışan", orgUnitId: units.Emea);
        await SetStatusAsync(gone.Id, EmployeeStatus.Terminated);

        var result = (await ExecuteQueryAsync(new GetDepartmentDistributionQuery())).Value;

        result.Sum(d => d.Count).ShouldBe(3);
        result.ShouldContain(new DepartmentDistributionItemDto(units.Sales, "Sales", 1));
        result.ShouldContain(new DepartmentDistributionItemDto(units.Emea, "Sales EMEA", 1));
        result.ShouldContain(new DepartmentDistributionItemDto(null, null, 1));
    }

    [Fact]
    public async Task Detail_ResolvesDepartmentAndTitleNamesFromOrgPlacement()
    {
        var tenantId = Guid.NewGuid();
        SetUser(tenantId, platformAdmin: true);
        var units = await SeedOrgUnitsAsync(tenantId);

        var db = ServiceProvider!.GetRequiredService<OrganizationDbContext>();
        var titleType = await db.OrgUnitTypes.AsNoTracking().SingleAsync(t => t.TenantId == tenantId && t.Code == OrgUnitTypeCodes.Title, TestContext.Current.CancellationToken);
        var titleUnit = OrgUnit.Create(tenantId, titleType.Id, units.Emea, "Satış Uzmanı").Value;
        db.OrgUnits.Add(titleUnit);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Deepest assignment on a Title unit: both the Title and its Department ancestor are named.
        var titled = await CreateAsync("Ünal", "Unvanlı", orgUnitId: titleUnit.Id);
        (titled.DepartmentName, titled.TitleName).ShouldBe(("Sales EMEA", "Satış Uzmanı"));
        var titledDetail = (await ExecuteQueryAsync(new GetEmployeeQuery(titled.Id))).Value;
        (titledDetail.DepartmentName, titledDetail.TitleName).ShouldBe(("Sales EMEA", "Satış Uzmanı"));

        // Placed directly on a Department: no Title level in the chain.
        var departmentOnly = await CreateAsync("Deniz", "Departman", orgUnitId: units.Sales);
        var departmentDetail = (await ExecuteQueryAsync(new GetEmployeeQuery(departmentOnly.Id))).Value;
        departmentDetail.DepartmentName.ShouldBe("Sales");
        departmentDetail.TitleName.ShouldBeNull();

        // Company-level or unassigned employees get neither.
        var unassigned = (await ExecuteQueryAsync(new GetEmployeeQuery((await CreateAsync("Boş", "Birim")).Id))).Value;
        unassigned.DepartmentName.ShouldBeNull();
        unassigned.TitleName.ShouldBeNull();
        var atRoot = (await ExecuteQueryAsync(new GetEmployeeQuery((await CreateAsync("Kök", "Birim", orgUnitId: units.Root)).Id))).Value;
        atRoot.DepartmentName.ShouldBeNull();
        atRoot.TitleName.ShouldBeNull();
    }

    [Fact]
    public async Task CrossModuleContracts_ResolveScopeSelectionsContactsAndUnits()
    {
        var tenantId = Guid.NewGuid();
        SetUser(tenantId, platformAdmin: true);
        var units = await SeedOrgUnitsAsync(tenantId);

        var orgUnitLookup = ServiceProvider!.GetRequiredService<IOrgUnitLookup>();
        (await orgUnitLookup.GetSelfAndDescendantIdsAsync(tenantId, [units.Root])).ShouldBe([units.Root, units.Sales, units.Emea], ignoreOrder: true);
        (await orgUnitLookup.GetSelfAndDescendantIdsAsync(tenantId, [units.Sales])).ShouldBe([units.Sales, units.Emea], ignoreOrder: true);
        (await orgUnitLookup.GetSelfAndDescendantIdsAsync(tenantId, [units.Closed])).ShouldBeEmpty();

        var lead = await CreateAsync("Lale", "Lider", orgUnitId: units.Sales, workEmail: "lale@acme.com");
        var member = await CreateAsync("Mehmet", "Üye", orgUnitId: units.Emea, managerId: lead.Id);
        var subMember = await CreateAsync("Selin", "Alt", orgUnitId: units.Root, managerId: member.Id);
        var outsider = await CreateAsync("Oğuz", "Dış", orgUnitId: units.Root);

        var scopeLookup = ServiceProvider!.GetRequiredService<IEmployeeScopeLookup>();
        (await scopeLookup.GetEmployeeIdsInScopeAsync(lead.Id, DataScope.Team, CancellationToken.None)).ShouldBe([member.Id, subMember.Id], ignoreOrder: true);
        (await scopeLookup.GetEmployeeIdsInScopeAsync(lead.Id, DataScope.Department, CancellationToken.None)).ShouldBe([lead.Id, member.Id], ignoreOrder: true);
        (await scopeLookup.GetEmployeeIdsInScopeAsync(lead.Id, DataScope.Location, CancellationToken.None)).ShouldBeEmpty();

        var directory = ServiceProvider!.GetRequiredService<IEmployeeDirectory>();
        (await directory.ResolveSelectionAsync(tenantId, new EmployeeSelection(null, Filter(orgUnitId: units.Root, includeSubUnits: true), [outsider.Id])))
            .ShouldBe([lead.Id, member.Id, subMember.Id], ignoreOrder: true);
        (await directory.ResolveSelectionAsync(tenantId, new EmployeeSelection(null, null, null))).ShouldBeEmpty();
        (await directory.ResolveSelectionAsync(tenantId, new EmployeeSelection(null, Filter(status: "bogus"), null))).ShouldBeEmpty();

        _scope.Visible = new HashSet<Guid> { lead.Id };
        (await directory.ResolveSelectionAsync(tenantId, new EmployeeSelection([lead.Id, member.Id], null, null))).ShouldBe([lead.Id]);
        _scope.Visible = null;

        var contact = (await directory.GetContactsAsync(tenantId, [lead.Id])).ShouldHaveSingleItem();
        contact.ShouldBe(new EmployeeContact(lead.Id, "Lale", "Lider", "lale@acme.com", null, null, nameof(EmployeeStatus.Active)));

        await SetStatusAsync(member.Id, EmployeeStatus.Terminated);
        (await directory.ListActiveEmployeeIdsInOrgUnitsAsync(tenantId, [units.Sales, units.Emea])).ShouldBe([lead.Id]);

        var employeeLookup = ServiceProvider!.GetRequiredService<IEmployeeLookup>();
        (await employeeLookup.AnyActiveInOrgUnitsAsync(tenantId, [units.Emea])).ShouldBeFalse();
        (await employeeLookup.AnyActiveInOrgUnitsAsync(tenantId, [units.Emea, units.Sales])).ShouldBeTrue();

        var linker = ServiceProvider!.GetRequiredService<IEmployeeUserLinker>();
        var userId = Guid.NewGuid();
        await linker.LinkUserAsync(tenantId, outsider.Id, userId);
        await linker.LinkUserAsync(tenantId, outsider.Id, userId);
        var reloaded = await ServiceProvider!.GetRequiredService<EmployeeDbContext>().Employees.AsNoTracking().SingleAsync(e => e.Id == outsider.Id);
        reloaded.UserId.ShouldBe(userId);
    }

    private sealed record OrgUnits(Guid Root, Guid Sales, Guid Emea, Guid Closed);

    private async Task<OrgUnits> SeedOrgUnitsAsync(Guid tenantId)
    {
        var db = ServiceProvider!.GetRequiredService<OrganizationDbContext>();
        var types = OrgUnitTypeCatalog.CreateAll(tenantId).Select(t => t.Value).ToList();
        db.OrgUnitTypes.AddRange(types);
        var companyType = types.Single(t => t.Code == OrgUnitTypeCodes.Company).Id;
        var departmentType = types.Single(t => t.Code == OrgUnitTypeCodes.Department).Id;

        var root = OrgUnit.Create(tenantId, companyType, null, "Acme").Value;
        var sales = OrgUnit.Create(tenantId, departmentType, root.Id, "Sales").Value;
        var emea = OrgUnit.Create(tenantId, departmentType, sales.Id, "Sales EMEA").Value;
        var closed = OrgUnit.Create(tenantId, departmentType, root.Id, "Closed").Value;
        closed.Deactivate().IsSuccess.ShouldBeTrue();
        db.OrgUnits.AddRange(root, sales, emea, closed);
        await db.SaveChangesAsync();
        return new OrgUnits(root.Id, sales.Id, emea.Id, closed.Id);
    }

    private static readonly DateOnly DefaultHireDate = new(2020, 1, 1);

    /// <summary>Hired (Active) by default - matches the old Faz 0 fixture behavior; pass asDraft: true to test the Draft quick-add path.</summary>
    private async Task<EmployeeDetailDto> CreateAsync(
        string firstName,
        string lastName,
        Guid? orgUnitId = null,
        Guid? managerId = null,
        string? nationalId = null,
        string? workEmail = null,
        string? workPhone = null,
        DateOnly? birthDate = null,
        DateOnly? hireDate = null,
        bool asDraft = false)
    {
        DateOnly? effectiveHireDate = asDraft ? null : hireDate ?? DefaultHireDate;
        var result = await ExecuteCommandAsync(new CreateEmployeeCommand(
            firstName, lastName, nationalId ?? Tckn.Next(), workEmail, null, workPhone, effectiveHireDate, birthDate, orgUnitId, managerId));
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
        return result.Value;
    }

    private async Task SetStatusAsync(Guid employeeId, EmployeeStatus status)
    {
        var repository = ServiceProvider!.GetRequiredService<IEmployeeRepository>();
        (await repository.GetByIdAsync(employeeId)).Value.SetStatus(status);
        await ServiceProvider!.GetRequiredService<EmployeeDbContext>().SaveChangesAsync();
    }

    private async Task<IReadOnlyList<EmployeeListItemDto>> ListAsync(string? q = null, string? sort = null, EmployeeFilter? filter = null)
    {
        var baseFilter = filter ?? Filter();
        var result = await ExecuteQueryAsync(new ListEmployeesQuery(new PagedQuery { Q = q, Sort = sort, PageSize = 100 }, baseFilter with { Q = q }));
        result.IsSuccess.ShouldBeTrue();
        return result.Value.Items;
    }

    private static EmployeeFilter Filter(Guid? orgUnitId = null, bool includeSubUnits = false, string? status = null, bool? hasUserAccount = null, Guid? managerId = null) =>
        new(null, orgUnitId, includeSubUnits, status, hasUserAccount, managerId);

    private static CreateEmployeeCommand NewCreate(string firstName, string lastName, string? nationalId = null, Guid? orgUnitId = null, Guid? managerId = null, string? workEmail = null) =>
        new(firstName, lastName, nationalId, workEmail, null, null, null, null, orgUnitId, managerId);

    private static UpdateEmployeeCommand UpdateOf(Guid id, string firstName, string lastName, Guid? orgUnitId = null, Guid? managerId = null) =>
        new(id, firstName, lastName, null, null, null, null, null, null, orgUnitId, managerId);

    private static void ShouldFail<T>(Result<T> result, string code)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(code);
    }

    /// <summary>
    /// Permissions are granted by the test double; "platformAdmin" only resets the data scope to Company. The platform flag itself is
    /// never set: without an active impersonation session it no longer bypasses authorization.
    /// </summary>
    private void SetUser(Guid tenantId, bool platformAdmin)
    {
        if (platformAdmin)
        {
            _scope.Visible = null;
        }

        ((CurrentUserAccessor)CurrentUser!).Set(new AuthenticatedUser(
            UserId: Guid.NewGuid(), EmployeeId: null, Email: "hr@test.local", DisplayName: "HR",
            Roles: new HashSet<string>(StringComparer.Ordinal), IsPlatformAdmin: false,
            ImpersonatorUserId: null, ApiKeyId: null, CorrelationId: null, IpAddress: "127.0.0.1"));
        TenantContextSetter!.BeginScope(tenantId);
    }
}

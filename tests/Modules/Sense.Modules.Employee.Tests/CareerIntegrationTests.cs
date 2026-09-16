using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Employee.Api;
using Sense.Modules.Employee.Application;
using Sense.Modules.Employee.Application.Career;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Organization.Api;
using Sense.Modules.Organization.Domain.OrgUnits;
using Sense.Modules.Organization.Domain.OrgUnitTypes;
using Sense.Modules.Organization.Infrastructure.Persistence;
using Sense.Shared.Contracts.Bulk;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Context;
using Sense.Shared.Infrastructure.Persistence.Outbox;
using Sense.Shared.Kernel.Results;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;
using ContractEvents = Sense.Modules.Employee.Contracts.Events;
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Modules.Employee.Tests;

/// <summary>
/// E2 career history end to end (Dispatcher → handlers → PostgreSQL, real Organization module): career endpoints,
/// unit-manager scope, every legacy write path (PUT, bulk, create) keeping history and denormalized fields in step,
/// lazy materialization for employees that predate career history, and ManagerChanged/CareerChanged through the outbox.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class CareerIntegrationTests : TestFixture
{
    private static readonly DateOnly HireDate = new(2024, 1, 1);
    private readonly FakeDataScope _scope = new();
    private readonly RecordingHandler<ContractEvents.ManagerChanged> _managerChanged = new();
    private readonly RecordingHandler<ContractEvents.CareerChanged> _careerChanged = new();
    private readonly RecordingHandler<ContractEvents.EmployeeHired> _hired = new();

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        new OrganizationModule().AddModule(services, configuration);
        new EmployeeModule().AddModule(services, configuration);
        services.AddSingleton<IPermissionService, GrantAllPermissions>();
        services.AddSingleton<IDataScopeService>(_scope);
        services.AddSingleton<IAccessSettingsService, AllAccessSettingsEnabled>();
        services.AddSingleton<IUserAccountLookup>(new FakeUserAccountLookup());
        services.AddSingleton<IIntegrationEventHandler<ContractEvents.ManagerChanged>>(_managerChanged);
        services.AddSingleton<IIntegrationEventHandler<ContractEvents.CareerChanged>>(_careerChanged);
        services.AddSingleton<IIntegrationEventHandler<ContractEvents.EmployeeHired>>(_hired);
    }

    protected override async Task OnDatabaseReadyAsync()
    {
        await ServiceProvider!.GetRequiredService<OrganizationDbContext>().Database.MigrateAsync();
        await ServiceProvider!.GetRequiredService<EmployeeDbContext>().Database.MigrateAsync();
    }

    [Fact]
    public async Task CareerEndpoints_AddEditDelete_KeepTimelineAndCardInStep_AndPublishEachChangeOnce()
    {
        var tenantId = SetUser();
        var org = await SeedOrgAsync(tenantId);
        var boss = await CreateAsync("Bora", "Başkan", org.Acme);
        var newBoss = await CreateAsync("Nil", "Yeni", org.Acme);
        var ali = await CreateAsync("Ali", "Kaya", org.Sales, boss.Id);

        ali.PositionStartDate.ShouldBe(HireDate);
        ali.LeaveAccrualStartDate.ShouldBe(HireDate);
        var initial = (await CareerAsync(ali.Id)).ShouldHaveSingleItem();
        initial.ShouldSatisfyAllConditions(
            r => r.Id.ShouldBe(ali.Id),
            r => r.ValidFrom.ShouldBe(HireDate),
            r => r.ChangeType.ShouldBe(CareerChangeType.Hire),
            r => r.IsCurrent.ShouldBeTrue(),
            r => r.ManagerDisplayName.ShouldBe("Bora Başkan"),
            r => r.Assignments.ShouldBe([new OrgAssignmentDto(org.DepartmentType, org.Sales), new OrgAssignmentDto(org.CompanyType, org.Acme)], ignoreOrder: true));
        (await CountAsync<ContractEvents.CareerChanged>(tenantId)).ShouldBe(3);
        (await CountAsync<ContractEvents.ManagerChanged>(tenantId)).ShouldBe(1);

        var promotedOn = new DateOnly(2025, 6, 1);
        var added = await ExecuteCommandAsync(new AddCareerRecordCommand(
            ali.Id, promotedOn, CareerChangeType.Promotion, EmploymentType.FullTime, newBoss.Id, "Müdür", Assign(org, org.Finance)));
        added.IsSuccess.ShouldBeTrue(added.IsFailure ? added.Error.Code : null);
        added.Value.Select(r => r.ValidFrom).ShouldBe([promotedOn, HireDate]);
        added.Value[1].ValidTo.ShouldBe(new DateOnly(2025, 5, 31));
        added.Value[0].IsCurrent.ShouldBeTrue();

        var card = (await ExecuteQueryAsync(new GetEmployeeQuery(ali.Id))).Value;
        (card.OrgUnitId, card.ManagerEmployeeId, card.EmploymentType, card.PositionStartDate).ShouldBe((org.Finance, newBoss.Id, EmploymentType.FullTime, promotedOn));

        var latestId = added.Value[0].Id;
        ShouldFail(await ExecuteCommandAsync(new AddCareerRecordCommand(ali.Id, promotedOn, CareerChangeType.Other, null, null, null, [])), EmployeeDomainErrors.CareerValidFromInvalid);
        ShouldFail(await ExecuteCommandAsync(new UpdateCareerRecordCommand(ali.Id, ali.Id, HireDate.AddDays(1), CareerChangeType.Hire, null, boss.Id, null, Assign(org, org.Sales))), EmployeeDomainErrors.CareerNotLatest);
        ShouldFail(await ExecuteCommandAsync(new AddCareerRecordCommand(ali.Id, Today, CareerChangeType.Other, null, null, null, [new OrgAssignmentDto(org.CompanyType, org.Sales)])), EmployeeErrors.OrgUnitNotFound);
        ShouldFail(await ExecuteCommandAsync(new AddCareerRecordCommand(ali.Id, Today, CareerChangeType.Other, null, Guid.NewGuid(), null, [])), EmployeeErrors.ManagerNotFound);

        // Moving the latest record's start keeps the manager but moves the change point: one CareerChanged + one ManagerChanged.
        var movedOn = new DateOnly(2025, 7, 1);
        var edited = await ExecuteCommandAsync(new UpdateCareerRecordCommand(ali.Id, latestId, movedOn, CareerChangeType.Promotion, EmploymentType.FullTime, newBoss.Id, "Müdür", Assign(org, org.Finance)));
        edited.Value[1].ValidTo.ShouldBe(new DateOnly(2025, 6, 30));

        // Identical edit: nothing published.
        (await ExecuteCommandAsync(new UpdateCareerRecordCommand(ali.Id, latestId, movedOn, CareerChangeType.Promotion, EmploymentType.FullTime, newBoss.Id, "Müdür", Assign(org, org.Finance)))).IsSuccess.ShouldBeTrue();

        var deleted = await ExecuteCommandAsync(new DeleteCareerRecordCommand(ali.Id, latestId));
        deleted.Value.ShouldHaveSingleItem().ShouldSatisfyAllConditions(r => r.ValidTo.ShouldBeNull(), r => r.IsCurrent.ShouldBeTrue());
        ShouldFail(await ExecuteCommandAsync(new DeleteCareerRecordCommand(ali.Id, ali.Id)), EmployeeDomainErrors.CareerLastRecord);
        (await ExecuteQueryAsync(new GetEmployeeQuery(ali.Id))).Value.ShouldSatisfyAllConditions(
            d => d.OrgUnitId.ShouldBe(org.Sales), d => d.ManagerEmployeeId.ShouldBe(boss.Id), d => d.PositionStartDate.ShouldBe(HireDate));

        (await CountAsync<ContractEvents.CareerChanged>(tenantId)).ShouldBe(3 + 3);
        (await CountAsync<ContractEvents.ManagerChanged>(tenantId)).ShouldBe(1 + 3);

        await ProcessOutboxAsync();
        _managerChanged.Received.Where(e => e.EmployeeId == ali.Id).Select(e => (e.OldManagerEmployeeId, e.NewManagerEmployeeId, e.EffectiveFrom)).ShouldBe(new (Guid?, Guid?, DateOnly)[]
        {
            (null, boss.Id, HireDate),
            (boss.Id, newBoss.Id, promotedOn),
            (boss.Id, newBoss.Id, movedOn),
            (newBoss.Id, boss.Id, movedOn),
        });
        _careerChanged.Received.Where(e => e.EmployeeId == ali.Id).Select(e => e.ChangeType).ShouldBe(
            [ContractEvents.CareerChangeType.Hire, ContractEvents.CareerChangeType.Promotion, ContractEvents.CareerChangeType.Promotion, ContractEvents.CareerChangeType.Hire]);
    }

    [Fact]
    public async Task UnitManager_OnlyMovesIntoAuthorizedUnits_ButKeepsUntouchedLevels_AndCannotReadOthers()
    {
        var tenantId = SetUser();
        var org = await SeedOrgAsync(tenantId);
        var member = await CreateAsync("Mehmet", "Üye", org.Sales);
        var outsider = await CreateAsync("Oğuz", "Dış", org.Finance);

        _scope.Visible = new HashSet<Guid> { member.Id };
        _scope.AccessibleOrgUnits = new HashSet<Guid> { org.Sales, org.Emea };

        // Company level (Acme) is above the authorized subtree but unchanged: allowed.
        var moved = await ExecuteCommandAsync(new AddCareerRecordCommand(member.Id, Today, CareerChangeType.LateralMove, null, null, null, Assign(org, org.Emea)));
        moved.IsSuccess.ShouldBeTrue(moved.IsFailure ? moved.Error.Code : null);

        ShouldFail(await ExecuteCommandAsync(new UpdateCareerRecordCommand(member.Id, moved.Value[0].Id, Today, CareerChangeType.LateralMove, null, null, null, Assign(org, org.Finance))), EmployeeErrors.OrgUnitOutOfScope);
        ShouldFail(await ExecuteQueryAsync(new GetEmployeeCareerQuery(outsider.Id)), ErrorCodes.OutOfScope);
        ShouldFail(await ExecuteCommandAsync(new AddCareerRecordCommand(outsider.Id, Today, CareerChangeType.Other, null, null, null, [])), ErrorCodes.OutOfScope);
    }

    [Fact]
    public async Task LegacyWritePaths_PutBulkAndDraftHire_GoThroughCareerHistory()
    {
        var tenantId = SetUser();
        var org = await SeedOrgAsync(tenantId);
        var boss = await CreateAsync("Bora", "Başkan", org.Acme);
        var ali = await CreateAsync("Ali", "Kaya", org.Sales);

        // PUT changing only the manager: ManagerChange record effective today; repeating the same body is a no-op.
        var put = await ExecuteCommandAsync(UpdateOf(ali, org.Sales, boss.Id));
        put.IsSuccess.ShouldBeTrue(put.IsFailure ? put.Error.Code : null);
        var timeline = await CareerAsync(ali.Id);
        timeline.Select(r => (r.ValidFrom, r.ChangeType)).ShouldBe(new (DateOnly, CareerChangeType)[] { (Today, CareerChangeType.ManagerChange), (HireDate, CareerChangeType.Hire) });
        (await ExecuteCommandAsync(UpdateOf(ali, org.Sales, boss.Id))).IsSuccess.ShouldBeTrue();

        // PUT moving the unit the same day amends that record instead of stacking another one.
        var movedPut = await ExecuteCommandAsync(UpdateOf(ali, org.Emea, boss.Id));
        movedPut.Value.OrgUnitId.ShouldBe(org.Emea);
        (await CareerAsync(ali.Id)).Count.ShouldBe(2);
        (await CountAsync<ContractEvents.ManagerChanged>(tenantId, ali.Id)).ShouldBe(1);
        (await CountAsync<ContractEvents.CareerChanged>(tenantId, ali.Id)).ShouldBe(3);

        // Bulk "Yönetici ata": same rule, per employee.
        var zeynep = await CreateAsync("Zeynep", "Arslan", org.Finance);
        var bulk = await ExecuteCommandAsync(new BulkAssignManagerCommand(new EmployeeSelection([zeynep.Id, ali.Id], null, null), boss.Id));
        bulk.Value.Succeeded.ShouldBe(2);
        (await CareerAsync(zeynep.Id)).Select(r => (r.ChangeType, r.ManagerEmployeeId)).ShouldBe(new (CareerChangeType, Guid?)[] { (CareerChangeType.ManagerChange, boss.Id), (CareerChangeType.Hire, null) });
        (await CountAsync<ContractEvents.ManagerChanged>(tenantId, zeynep.Id)).ShouldBe(1);
        (await CountAsync<ContractEvents.ManagerChanged>(tenantId, ali.Id)).ShouldBe(1);

        // Quick-add Draft, later hired with a unit and an earlier accrual date: one Hire record on the hire date.
        var draft = await ExecuteCommandAsync(new CreateEmployeeCommand("Deniz", "Taslak", null, null, null, null, null, null, null, null));
        (await CareerAsync(draft.Value.Id)).ShouldBeEmpty();
        var accrual = new DateOnly(2023, 3, 1);
        var hired = await ExecuteCommandAsync(new UpdateEmployeeCommand(
            draft.Value.Id, "Deniz", "Taslak", null, null, null, null, HireDate, null, org.Sales, null, LeaveAccrualStartDate: accrual));
        hired.IsSuccess.ShouldBeTrue(hired.IsFailure ? hired.Error.Code : null);
        (hired.Value.Status, hired.Value.LeaveAccrualStartDate, hired.Value.PositionStartDate).ShouldBe((nameof(EmployeeStatus.Active), accrual, HireDate));
        (await CareerAsync(draft.Value.Id)).ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            r => r.ChangeType.ShouldBe(CareerChangeType.Hire), r => r.ValidFrom.ShouldBe(HireDate));

        await ProcessOutboxAsync();
        _hired.Received.Single(e => e.EmployeeId == draft.Value.Id).LeaveAccrualStartDate.ShouldBe(accrual);
    }

    [Fact]
    public async Task EmployeeWithoutStoredHistory_ShowsImplicitRecord_AndFirstWriteMaterializesIt()
    {
        var tenantId = SetUser();
        var org = await SeedOrgAsync(tenantId);
        var boss = await CreateAsync("Bora", "Başkan", org.Acme);

        // As written before E2 (or by the seeder): denormalized unit/manager, no career rows.
        var legacy = EmployeeEntity.Create(tenantId, new EmployeeInfo("Eski", "Kayıt", HireDate: new DateOnly(2019, 4, 1)), orgUnitId: org.Emea, managerEmployeeId: boss.Id).Value;
        var db = ServiceProvider!.GetRequiredService<EmployeeDbContext>();
        db.Employees.Add(legacy);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var implicitRecord = (await CareerAsync(legacy.Id)).ShouldHaveSingleItem();
        implicitRecord.ShouldSatisfyAllConditions(
            r => r.Id.ShouldBe(legacy.Id),
            r => r.ValidFrom.ShouldBe(new DateOnly(2019, 4, 1)),
            r => r.ManagerEmployeeId.ShouldBe(boss.Id),
            r => r.Assignments.ShouldBe([new OrgAssignmentDto(org.DepartmentType, org.Emea), new OrgAssignmentDto(org.CompanyType, org.Acme)], ignoreOrder: true));
        (await db.Set<CareerRecord>().CountAsync(r => r.EmployeeId == legacy.Id)).ShouldBe(0);

        // Editing the implicit record by its id materializes it and applies the edit.
        var edited = await ExecuteCommandAsync(new UpdateCareerRecordCommand(
            legacy.Id, legacy.Id, new DateOnly(2019, 4, 1), CareerChangeType.Hire, EmploymentType.PartTime, boss.Id, "Aktarım", implicitRecord.Assignments));
        edited.IsSuccess.ShouldBeTrue(edited.IsFailure ? edited.Error.Code : null);
        db.ChangeTracker.Clear();
        var stored = await db.Set<CareerRecord>().AsNoTracking().SingleAsync(r => r.EmployeeId == legacy.Id);
        (stored.Id, stored.EmploymentType, stored.Note, stored.Assignments.Count).ShouldBe((legacy.Id, EmploymentType.PartTime, "Aktarım", 2));
        (await ExecuteQueryAsync(new GetEmployeeQuery(legacy.Id))).Value.EmploymentType.ShouldBe(EmploymentType.PartTime);
        (await CountAsync<ContractEvents.CareerChanged>(tenantId, legacy.Id)).ShouldBe(1);
        (await CountAsync<ContractEvents.ManagerChanged>(tenantId, legacy.Id)).ShouldBe(0);
    }

    private sealed record Org(Guid CompanyType, Guid DepartmentType, Guid Acme, Guid Sales, Guid Emea, Guid Finance);

    private async Task<Org> SeedOrgAsync(Guid tenantId)
    {
        var db = ServiceProvider!.GetRequiredService<OrganizationDbContext>();
        var types = OrgUnitTypeCatalog.CreateAll(tenantId).Select(t => t.Value).ToList();
        db.OrgUnitTypes.AddRange(types);
        var company = types.Single(t => t.Code == OrgUnitTypeCodes.Company).Id;
        var department = types.Single(t => t.Code == OrgUnitTypeCodes.Department).Id;

        var acme = OrgUnit.Create(tenantId, company, null, "Acme").Value;
        var sales = OrgUnit.Create(tenantId, department, acme.Id, "Sales").Value;
        var emea = OrgUnit.Create(tenantId, department, sales.Id, "Sales EMEA").Value;
        var finance = OrgUnit.Create(tenantId, department, acme.Id, "Finance").Value;
        db.OrgUnits.AddRange(acme, sales, emea, finance);
        await db.SaveChangesAsync();
        return new Org(company, department, acme.Id, sales.Id, emea.Id, finance.Id);
    }

    private static IReadOnlyList<OrgAssignmentDto> Assign(Org org, Guid department) =>
        [new OrgAssignmentDto(org.CompanyType, org.Acme), new OrgAssignmentDto(org.DepartmentType, department)];

    private async Task<EmployeeDetailDto> CreateAsync(string firstName, string lastName, Guid? orgUnitId = null, Guid? managerId = null)
    {
        var result = await ExecuteCommandAsync(new CreateEmployeeCommand(firstName, lastName, Tckn.Next(), null, null, null, HireDate, null, orgUnitId, managerId));
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
        return result.Value;
    }

    private static UpdateEmployeeCommand UpdateOf(EmployeeDetailDto e, Guid? orgUnitId, Guid? managerId) =>
        new(e.Id, e.FirstName, e.LastName, e.NationalId, e.WorkEmail, e.PersonalEmail, e.WorkPhone, e.HireDate, e.BirthDate, orgUnitId, managerId);

    private async Task<IReadOnlyList<CareerRecordDto>> CareerAsync(Guid employeeId)
    {
        var result = await ExecuteQueryAsync(new GetEmployeeCareerQuery(employeeId));
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
        return result.Value;
    }

    private async Task<int> CountAsync<TEvent>(Guid tenantId, Guid? employeeId = null)
    {
        var type = EventTypeRegistry.NameOf(typeof(TEvent));
        var messages = await ServiceProvider!.GetRequiredService<EmployeeDbContext>().OutboxMessages.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.Type == type)
            .Select(m => m.Payload)
            .ToListAsync();
        return employeeId is { } id
            ? messages.Count(p => p.Replace(" ", string.Empty, StringComparison.Ordinal).Contains($"\"employeeId\":\"{id}\"", StringComparison.OrdinalIgnoreCase))
            : messages.Count;
    }

    private async Task ProcessOutboxAsync()
    {
        using var scope = ServiceProvider!.CreateScope();
        await scope.ServiceProvider.GetRequiredService<OutboxProcessor<EmployeeDbContext>>().ProcessAsync();
    }

    private static void ShouldFail<T>(Result<T> result, string code)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(code);
    }

    private Guid SetUser()
    {
        var tenantId = Guid.NewGuid();
        _scope.Visible = null;
        _scope.AccessibleOrgUnits = null;
        ((CurrentUserAccessor)CurrentUser!).Set(new AuthenticatedUser(
            UserId: Guid.NewGuid(), EmployeeId: null, Email: "hr@test.local", DisplayName: "HR",
            Roles: new HashSet<string>(StringComparer.Ordinal), IsPlatformAdmin: false,
            ImpersonatorUserId: null, ApiKeyId: null, CorrelationId: null, IpAddress: "127.0.0.1"));
        TenantContextSetter!.BeginScope(tenantId);
        return tenantId;
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Employee.Api;
using Sense.Modules.Employee.Application.Career;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Application.Import;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Organization.Api;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Organization.Domain.OrgUnits;
using Sense.Modules.Organization.Domain.OrgUnitTypes;
using Sense.Modules.Organization.Infrastructure.Persistence;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Contracts.Import;
using Sense.Shared.Infrastructure.Context;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Employee.Tests;

[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class PersonnelNumberIntegrationTests : TestFixture
{
    private readonly FakeDataScope _scope = new();
    private bool _canManage = true;
    protected override void ConfigureServices(IServiceCollection services)
    {
        var config = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        new OrganizationModule().AddModule(services, config);
        new EmployeeModule().AddModule(services, config);
        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call => call.ArgAt<string>(1) != EmployeePermissions.PersonnelNumberManageKey || _canManage);
        services.AddSingleton(permissions);
        services.AddSingleton<IDataScopeService>(_scope);
        services.AddSingleton<IAccessSettingsService, AllAccessSettingsEnabled>();
        services.AddSingleton<Sense.Modules.Identity.Contracts.IUserAccountLookup, FakeUserAccountLookup>();
    }
    protected override async Task OnDatabaseReadyAsync()
    {
        await ServiceProvider!.GetRequiredService<OrganizationDbContext>().Database.MigrateAsync();
        await ServiceProvider!.GetRequiredService<EmployeeDbContext>().Database.MigrateAsync();
    }
    private void User(Guid tenantId)
    {
        ((CurrentUserAccessor)CurrentUser!).Set(new AuthenticatedUser(Guid.NewGuid(), null, "hr@test.local", "HR", new HashSet<string>(), false, null, null, null, "127.0.0.1"));
        TenantContextSetter!.BeginScope(tenantId);
    }
    private async Task<(Guid Company, Guid Department, Guid Title)> Organization()
    {
        var db = ServiceProvider!.GetRequiredService<OrganizationDbContext>();
        var tenantId = TenantContextAccessor!.TenantId;
        var types = OrgUnitTypeCatalog.CreateAll(tenantId).Select(r => r.Value).ToList();
        db.OrgUnitTypes.AddRange(types);
        var company = OrgUnit.Create(tenantId, types.Single(t => t.Code == OrgUnitTypeCodes.Company).Id, null, "Company").Value;
        var department = OrgUnit.Create(tenantId, types.Single(t => t.Code == OrgUnitTypeCodes.Department).Id, company.Id, "Personnel area").Value;
        var title = OrgUnit.Create(tenantId, types.Single(t => t.Code == OrgUnitTypeCodes.Title).Id, department.Id, "Position").Value;
        db.OrgUnits.AddRange(company, department, title); await db.SaveChangesAsync();
        return (company.Id, department.Id, title.Id);
    }
    private async Task<PersonnelNumberRangeDto> Range(string code, string start = "10000000", string end = "10000002", PersonnelNumberAssignmentMode mode = PersonnelNumberAssignmentMode.Internal)
    {
        var result = await ExecuteCommandAsync(new CreatePersonnelNumberRangeCommand(code, code, start, end, mode));
        result.IsSuccess.ShouldBeTrue(result.Error.Code); return result.Value;
    }
    private Task<Sense.Shared.Kernel.Results.Result<EmployeeDetailDto>> Hire(Guid? orgUnitId, string? number = null) => ExecuteCommandAsync(new CreateEmployeeCommand("Ali", "Yılmaz", null, null, null, null, new DateOnly(2020, 1, 1), null, orgUnitId, null, SicilNumarasi: number));

    [Fact]
    public async Task Ranges_RejectOverlapInactiveAndShrink_AndEnforceReferences()
    {
        User(Guid.NewGuid()); var org = await Organization(); var r = await Range("01");
        (await ExecuteCommandAsync(new CreatePersonnelNumberRangeCommand("02", "Overlap", "10000002", "10000003", PersonnelNumberAssignmentMode.External))).Error.Code.ShouldBe("Employee.PersonnelNumber.OverlappingRange");
        (await ExecuteCommandAsync(new UpdatePersonnelNumberRangeCommand(r.Id, "Shrink", "10000001"))).IsFailure.ShouldBeTrue();
        (await ExecuteCommandAsync(new CreatePersonnelNumberRuleCommand(org.Company, PersonnelNumberOperation.Hire, r.Id))).IsSuccess.ShouldBeTrue();
        (await ExecuteCommandAsync(new SetPersonnelNumberRangeActiveCommand(r.Id, false))).Error.Code.ShouldBe("Employee.PersonnelNumber.RangeInUse");
        var rule = (await ExecuteQueryAsync(new ListPersonnelNumberRulesQuery())).Value.Single();
        (await ExecuteCommandAsync(new UpdatePersonnelNumberRuleCommand(rule.Id, r.Id, false))).IsSuccess.ShouldBeTrue();
        (await ExecuteCommandAsync(new SetPersonnelNumberRangeActiveCommand(r.Id, false))).IsSuccess.ShouldBeTrue();
        (await ExecuteCommandAsync(new CreatePersonnelNumberRangeCommand("03", "Overlap", "10000000", "10000001", PersonnelNumberAssignmentMode.Internal))).IsFailure.ShouldBeTrue();
    }
    [Fact]
    public async Task Internal_PreviewDoesNotReserve_ExhaustionAndImmutability()
    {
        User(Guid.NewGuid()); var org = await Organization(); var range = await Range("01", end: "10000000");
        await ExecuteCommandAsync(new CreatePersonnelNumberRuleCommand(org.Company, PersonnelNumberOperation.Hire, range.Id));
        (await ExecuteQueryAsync(new PreviewPersonnelNumberQuery(org.Title))).Value.NextNumber.ShouldBe("10000000");
        (await ExecuteQueryAsync(new PreviewPersonnelNumberQuery(org.Title))).Value.NextNumber.ShouldBe("10000000");
        (await Hire(org.Title, "10000000")).Error.Code.ShouldBe("Employee.PersonnelNumber.InternalSupplied");
        var e = (await Hire(org.Title)).Value; e.SicilNumarasi.ShouldBe("10000000");
        (await Hire(org.Title)).Error.Code.ShouldBe("Employee.PersonnelNumber.Exhausted");
        (await ExecuteCommandAsync(new UpdateEmployeeCommand(e.Id, e.FirstName, e.LastName, null, null, null, null, e.HireDate, null, org.Title, null, SicilNumarasi: "10000001"))).Error.Code.ShouldBe("Employee.PersonnelNumber.Immutable");
        (await ExecuteQueryAsync(new GetEmployeeQuery(e.Id))).Value.SicilNumarasi.ShouldBe("10000000");
    }
    [Fact]
    public async Task PersonnelAreaOverride_MostSpecificAndCompanyFallback_ExternalValidation()
    {
        User(Guid.NewGuid()); var org = await Organization(); var standard = await Range("01");
        var external = await Range("02", "00000001", "00000009", PersonnelNumberAssignmentMode.External);
        await ExecuteCommandAsync(new CreatePersonnelNumberRuleCommand(org.Company, PersonnelNumberOperation.Hire, standard.Id));
        await ExecuteCommandAsync(new CreatePersonnelNumberRuleCommand(org.Company, PersonnelNumberOperation.Hire, external.Id, org.Department));
        var preview = (await ExecuteQueryAsync(new PreviewPersonnelNumberQuery(org.Title))).Value;
        preview.RangeId.ShouldBe(external.Id); preview.PersonnelAreaId.ShouldBe(org.Department);
        (await ExecuteQueryAsync(new PreviewPersonnelNumberQuery(org.Company))).Value.RangeId.ShouldBe(standard.Id);
        (await Hire(org.Title)).Error.Code.ShouldBe("Employee.PersonnelNumber.ExternalInvalid");
        (await Hire(org.Title, "1")).IsFailure.ShouldBeTrue();
        (await Hire(org.Title, "00000010")).IsFailure.ShouldBeTrue();
        (await Hire(org.Title, "00000001")).Value.SicilNumarasi.ShouldBe("00000001");
        (await Hire(org.Title, "00000001")).IsFailure.ShouldBeTrue();
        (await ExecuteCommandAsync(new CreatePersonnelNumberRuleCommand(org.Company, PersonnelNumberOperation.Hire, standard.Id, org.Department))).IsFailure.ShouldBeTrue();
    }
    [Fact]
    public async Task TenantIsolationAndUnconfiguredCompatibility()
    {
        User(Guid.NewGuid()); var first = await Organization(); var range = await Range("01");
        await ExecuteCommandAsync(new CreatePersonnelNumberRuleCommand(first.Company, PersonnelNumberOperation.Hire, range.Id));
        (await Hire(first.Title)).Value.SicilNumarasi.ShouldBe("10000000");
        User(Guid.NewGuid()); var second = await Organization();
        (await ExecuteQueryAsync(new PreviewPersonnelNumberQuery(second.Title))).Value.Configured.ShouldBeFalse();
        (await ExecuteCommandAsync(new CreatePersonnelNumberRuleCommand(second.Company, PersonnelNumberOperation.Hire, range.Id))).IsFailure.ShouldBeTrue();
        (await Hire(second.Title, "10000000")).Value.SicilNumarasi.ShouldBe("10000000");
        (await Hire(second.Title, "LEGACY-A")).Value.SicilNumarasi.ShouldBe("LEGACY-A");
        (await ExecuteQueryAsync(new ListPersonnelNumberRangesQuery(true))).Value.ShouldBeEmpty();
        await Range("01"); (await Hire(second.Title)).Error.Code.ShouldBe("Employee.PersonnelNumber.MissingRule");
    }
    [Fact]
    public async Task PreviewAndCompanies_RespectPlacementScope_AndManagementPermission()
    {
        User(Guid.NewGuid()); var org = await Organization(); var range = await Range("01");
        await ExecuteCommandAsync(new CreatePersonnelNumberRuleCommand(org.Company, PersonnelNumberOperation.Hire, range.Id));
        _canManage = false; _scope.AccessibleOrgUnits = new HashSet<Guid> { org.Title };
        (await ExecuteQueryAsync(new ListPersonnelNumberRangesQuery())).IsFailure.ShouldBeTrue();
        var companies = (await ExecuteQueryAsync(new ListPersonnelNumberCompaniesQuery())).Value;
        companies.Single().Id.ShouldBe(org.Company); companies.Single().PersonnelAreas.Single().Id.ShouldBe(org.Title);
        (await ExecuteQueryAsync(new PreviewPersonnelNumberQuery(org.Company))).IsFailure.ShouldBeTrue();
        (await ExecuteQueryAsync(new PreviewPersonnelNumberQuery(org.Title))).Value.NextNumber.ShouldBe("10000000");
        _scope.AccessibleOrgUnits = new HashSet<Guid>();
        (await ExecuteQueryAsync(new ListPersonnelNumberCompaniesQuery())).Value.ShouldBeEmpty();
    }
    [Fact]
    public async Task ConcurrentAllocation_SeparateConnections_UniqueAndSequential()
    {
        var tenantId = Guid.NewGuid(); User(tenantId); var org = await Organization(); var range = await Range("01", end: "10000009");
        await ExecuteCommandAsync(new CreatePersonnelNumberRuleCommand(org.Company, PersonnelNumberOperation.Hire, range.Id));
        var rootDb = ServiceProvider!.GetRequiredService<EmployeeDbContext>();
        var units = Substitute.For<IOrgUnitLookup>();
        var actual = ServiceProvider!.GetRequiredService<IOrgUnitLookup>();
        var types = await actual.ListActiveTypesAsync(tenantId);
        var activeUnits = await actual.ListActiveUnitsAsync(tenantId);
        units.ListActiveTypesAsync(tenantId, Arg.Any<CancellationToken>()).Returns(types);
        units.ListActiveUnitsAsync(tenantId, Arg.Any<CancellationToken>()).Returns(activeUnits);
        var options = new DbContextOptionsBuilder<EmployeeDbContext>().UseNpgsql(rootDb.Database.GetConnectionString()).UseSnakeCaseNamingConvention().Options;
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            var context = new TenantContext(); using var scope = context.BeginScope(tenantId);
            await using var db = new EmployeeDbContext(options, context);
            var service = new PersonnelNumberService(db, context, new OrgPlacementLoader(context, units));
            string? number = null;
            await db.ExecuteInTransactionAsync(async ct =>
            {
                var assigned = await service.AssignAsync(org.Title, PersonnelNumberOperation.Hire, null, ct);
                assigned.IsSuccess.ShouldBeTrue(); number = assigned.Value;
                db.Employees.Add(Domain.Employees.Employee.Create(tenantId, new EmployeeInfo("Ali", "Yılmaz", SicilNumarasi: number), orgUnitId: org.Title).Value);
                await db.SaveChangesAsync(ct);
            });
            return number;
        }));
        results.Distinct().Count().ShouldBe(5);
        results.Order().ShouldBe(new[] { "10000000", "10000001", "10000002", "10000003", "10000004" });
    }

    private static ImportRow Row(int row, string nationalId, string? main = null, string? legacy = null) => new(row, new Dictionary<string, ImportCell>
    {
        [EmployeeImportColumns.FirstName] = new("Ali"), [EmployeeImportColumns.LastName] = new("Yılmaz"),
        [EmployeeImportColumns.NationalId] = new(nationalId), [EmployeeImportColumns.OrgUnit("Company")] = new("Company"),
        [EmployeeImportColumns.SicilNumarasi] = new(main), [EmployeeImportColumns.LegacySourceSystem] = new(legacy is null ? null : "Old ERP"),
        [EmployeeImportColumns.LegacyPersonnelNumber] = new(legacy),
    });
    private Task<ImportBatchResult> Import(params ImportRow[] rows) => ServiceProvider!.GetServices<IImportRowHandler>().Single(h => h.TemplateCode == EmployeeImportColumns.TemplateCode).ProcessAsync(new ImportBatch(ImportMode.Upsert, ImportFailurePolicy.AllOrNothing, rows.SelectMany(r => r.Values.Keys).Distinct().ToList(), rows));
    [Fact]
    public async Task Import_InternalBatchNumbersAndLegacyMapping_AndAllOrNothingRollback()
    {
        User(Guid.NewGuid()); var org = await Organization(); var range = await Range("01", end: "10000001");
        await ExecuteCommandAsync(new CreatePersonnelNumberRuleCommand(org.Company, PersonnelNumberOperation.Import, range.Id));
        var failed = await Import(Row(1, "10000000146", legacy: "ABC-1"), Row(2, "10000000214", main: "10000001"));
        failed.Applied.ShouldBeFalse();
        var db = ServiceProvider!.GetRequiredService<EmployeeDbContext>();
        (await db.Employees.CountAsync()).ShouldBe(0);
        (await db.Set<LegacyPersonnelNumber>().CountAsync()).ShouldBe(0);
        (await ExecuteQueryAsync(new ListPersonnelNumberRangesQuery())).Value.Single().LastAssignedNumber.ShouldBeNull();
        var valid = await Import(Row(1, "10000000146", legacy: "ABC-1"), Row(2, "10000000214", legacy: "ABC-2"));
        valid.Applied.ShouldBeTrue(); valid.Rows.All(r => r.Outcome == ImportRowOutcome.Inserted).ShouldBeTrue();
        (await db.Employees.Select(e => e.SicilNumarasi).OrderBy(n => n).ToListAsync()).ShouldBe(new[] { "10000000", "10000001" });
        (await db.Set<LegacyPersonnelNumber>().CountAsync()).ShouldBe(2);
        var detail = (await ExecuteQueryAsync(new GetEmployeeQuery(valid.Rows[0].TargetEntityId!.Value))).Value;
        detail.LegacyPersonnelNumbers!.Single().PersonnelNumber.ShouldBe("ABC-1");
    }
    [Fact]
    public async Task Import_ExternalPreservesLeadingZeros_RejectsDuplicateAndLegacyConflict()
    {
        User(Guid.NewGuid()); var org = await Organization(); var range = await Range("90", "00000001", "00000009", PersonnelNumberAssignmentMode.External);
        await ExecuteCommandAsync(new CreatePersonnelNumberRuleCommand(org.Company, PersonnelNumberOperation.Import, range.Id));
        var duplicate = await Import(Row(1, "10000000146", "00000001"), Row(2, "10000000214", "00000001"));
        duplicate.Applied.ShouldBeFalse();
        (await ServiceProvider!.GetRequiredService<EmployeeDbContext>().Employees.CountAsync()).ShouldBe(0);
        var valid = await Import(Row(1, "10000000146", "00000001", "ABC-1"));
        valid.Rows.Single().Outcome.ShouldBe(ImportRowOutcome.Inserted);
        var conflict = await Import(Row(2, "10000000214", "00000002", "ABC-1"));
        conflict.Applied.ShouldBeFalse(); conflict.Rows.Single().Errors.Single().Code.ShouldBe("Employee.PersonnelNumber.LegacyConflict");
        (await ExecuteQueryAsync(new GetEmployeeQuery(valid.Rows.Single().TargetEntityId!.Value))).Value.SicilNumarasi.ShouldBe("00000001");
    }
    [Fact]
    public async Task Import_SkipInvalidRowDoesNotClaimItsLegacyNumber()
    {
        User(Guid.NewGuid()); var org = await Organization(); var range = await Range("90", "00000001", "00000009", PersonnelNumberAssignmentMode.External);
        await ExecuteCommandAsync(new CreatePersonnelNumberRuleCommand(org.Company, PersonnelNumberOperation.Import, range.Id));
        var rows = new[] { Row(1, "10000000146", "00000010", "ABC-1"), Row(2, "10000000214", "00000001", "ABC-1") };
        var result = await ServiceProvider!.GetServices<IImportRowHandler>().Single(h => h.TemplateCode == EmployeeImportColumns.TemplateCode)
            .ProcessAsync(new ImportBatch(ImportMode.Upsert, ImportFailurePolicy.SkipInvalidRows, rows.SelectMany(r => r.Values.Keys).Distinct().ToList(), rows));
        result.Applied.ShouldBeTrue();
        result.Rows[0].Outcome.ShouldBe(ImportRowOutcome.Error);
        result.Rows[0].Errors.Single().Code.ShouldBe("Employee.PersonnelNumber.ExternalInvalid");
        result.Rows[1].Outcome.ShouldBe(ImportRowOutcome.Inserted, string.Join(", ", result.Rows[1].Errors.Select(e => e.Code)));
        var db = ServiceProvider!.GetRequiredService<EmployeeDbContext>();
        (await db.Employees.CountAsync()).ShouldBe(1);
        var mapping = await db.Set<LegacyPersonnelNumber>().SingleAsync();
        mapping.PersonnelNumber.ShouldBe("ABC-1"); mapping.EmployeeId.ShouldBe(result.Rows[1].TargetEntityId!.Value);
    }
}

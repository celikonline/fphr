using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Employee.Application;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Application.Import;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Import;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Kernel.Results;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Modules.Employee.Tests;

/// <summary>
/// Regression: the same work e-mail (differing only by case) could be attached to several employees. Now rejected
/// on create/update (409 employee.work_email_taken) and per row on import.
/// </summary>
public sealed class EmployeeWorkEmailUniquenessTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static EmployeeReferenceValidator Validator(IEmployeeRepository employees)
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        var dataScope = Substitute.For<IDataScopeService>();
        dataScope.GetAccessibleOrgUnitIdsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlySet<Guid>?>(null));
        return new EmployeeReferenceValidator(tenant, employees, Substitute.For<IOrgUnitLookup>(), dataScope);
    }

    [Fact]
    public async Task Create_WithWorkEmailUsedByAnotherEmployee_IsConflict()
    {
        var employees = Substitute.For<IEmployeeRepository>();
        employees.WorkEmailTakenAsync("X@Example.com", null, Arg.Any<CancellationToken>()).Returns(true);

        var result = await Validator(employees).ValidateAsync(
            new EmployeeInfo("A", "B", WorkEmail: "  X@Example.com "), null, null, existing: null, "perm", CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(EmployeeErrors.WorkEmailTaken);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
    }

    [Fact]
    public async Task Create_WithFreeWorkEmail_Passes()
    {
        var employees = Substitute.For<IEmployeeRepository>();

        var result = await Validator(employees).ValidateAsync(
            new EmployeeInfo("A", "B", WorkEmail: "free@example.com"), null, null, existing: null, "perm", CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Update_KeepingOwnEmailWithDifferentCase_DoesNotQueryOrConflict()
    {
        var existing = EmployeeEntity.Create(TenantId, new EmployeeInfo("A", "B", WorkEmail: "ali@example.com")).Value;
        var employees = Substitute.For<IEmployeeRepository>();
        employees.WorkEmailTakenAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await Validator(employees).ValidateAsync(
            new EmployeeInfo("A", "B", WorkEmail: "ALI@example.com"), null, null, existing, "perm", CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await employees.DidNotReceiveWithAnyArgs().WorkEmailTakenAsync(default!, default, default);
    }

    [Fact]
    public async Task Update_ChangingToAnotherEmployeesEmail_IsConflict()
    {
        var existing = EmployeeEntity.Create(TenantId, new EmployeeInfo("A", "B", WorkEmail: "ali@example.com")).Value;
        var employees = Substitute.For<IEmployeeRepository>();
        employees.WorkEmailTakenAsync("veli@example.com", existing.Id, Arg.Any<CancellationToken>()).Returns(true);

        var result = await Validator(employees).ValidateAsync(
            new EmployeeInfo("A", "B", WorkEmail: "veli@example.com"), null, null, existing, "perm", CancellationToken.None);

        result.Error.Code.ShouldBe(EmployeeErrors.WorkEmailTaken);
    }

    // ---------- import ----------

    private const string TcknA = "10000000146";
    private const string TcknB = "99123456740";

    private static EmployeeImportSnapshot Snapshot(params EmployeeRow[] rows) =>
        new(rows, [], [], null, null, [], canViewSensitiveCustomFields: false);

    private static EmployeeRow Row(Guid id, string nationalId, string? email, EmployeeStatus status = EmployeeStatus.Active) =>
        new(id, "Mevcut", "Kisi", "Mevcut Kisi", nationalId, email, null, null, null, null, status, null, null, null);

    private static ImportRow Import(int number, string nationalId, string email) => new(number, new Dictionary<string, ImportCell>
    {
        [EmployeeImportColumns.NationalId] = new(nationalId),
        [EmployeeImportColumns.FirstName] = new("Yeni"),
        [EmployeeImportColumns.LastName] = new("Kisi"),
        [EmployeeImportColumns.WorkEmail] = new(email),
    });

    [Fact]
    public void Import_RowUsingAnotherEmployeesEmail_CaseInsensitively_IsRejected()
    {
        var validator = new EmployeeImportRowValidator(ImportMode.Upsert, Snapshot(Row(Guid.NewGuid(), TcknB, "Taken@Example.com")));

        var outcome = validator.Validate(Import(2, TcknA, "taken@example.COM"));

        outcome.Errors.ShouldContain(e => e.Code == ImportErrorCodes.DuplicateWorkEmail && e.Column == EmployeeImportColumns.WorkEmail);
    }

    [Fact]
    public void Import_SameEmailTwiceInFile_RejectsSecondRow()
    {
        var validator = new EmployeeImportRowValidator(ImportMode.Upsert, Snapshot());

        var first = validator.Validate(Import(2, TcknA, "dup@example.com"));
        var second = validator.Validate(Import(3, TcknB, "DUP@example.com"));

        first.Errors.ShouldNotContain(e => e.Code == ImportErrorCodes.DuplicateWorkEmail);
        second.Errors.ShouldContain(e => e.Code == ImportErrorCodes.DuplicateWorkEmail);
    }

    [Fact]
    public void Import_UpdatingEmployeeWithItsOwnEmail_IsAllowed()
    {
        var owner = Row(Guid.NewGuid(), TcknA, "own@example.com");
        var validator = new EmployeeImportRowValidator(ImportMode.Upsert, Snapshot(owner));

        var outcome = validator.Validate(Import(2, TcknA, "OWN@example.com"));

        outcome.Errors.ShouldNotContain(e => e.Code == ImportErrorCodes.DuplicateWorkEmail);
    }

    [Fact]
    public void Import_EmailOfTerminatedEmployee_CanBeReused()
    {
        var validator = new EmployeeImportRowValidator(
            ImportMode.Upsert, Snapshot(Row(Guid.NewGuid(), TcknB, "old@example.com", EmployeeStatus.Terminated)));

        var outcome = validator.Validate(Import(2, TcknA, "old@example.com"));

        outcome.Errors.ShouldNotContain(e => e.Code == ImportErrorCodes.DuplicateWorkEmail);
    }
}

/// <summary>The repository query against real PostgreSQL: case-insensitive, tenant-scoped, excludes self and terminated.</summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class EmployeeWorkEmailRepositoryTests : TestFixture
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        services.AddModuleDbContext<EmployeeDbContext>(configuration, EmployeeDbContext.SchemaName);
    }

    [Fact]
    public async Task WorkEmailTakenAsync_IsCaseInsensitive_TenantScoped_AndIgnoresSelf()
    {
        var db = ServiceProvider!.GetRequiredService<EmployeeDbContext>();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var repository = new EmployeeRepository(db);

        using (TenantContextSetter!.BeginScope(tenantA))
        {
            var active = EmployeeEntity.Create(tenantA, new EmployeeInfo("Ali", "Kaya", WorkEmail: "Ali.Kaya@Example.com")).Value;
            db.Employees.Add(active);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            (await repository.WorkEmailTakenAsync("ali.kaya@example.com", null, TestContext.Current.CancellationToken)).ShouldBeTrue();
            (await repository.WorkEmailTakenAsync("ALI.KAYA@EXAMPLE.COM", null, TestContext.Current.CancellationToken)).ShouldBeTrue();
            (await repository.WorkEmailTakenAsync("ali.kaya@example.com", active.Id, TestContext.Current.CancellationToken)).ShouldBeFalse();
            (await repository.WorkEmailTakenAsync("other@example.com", null, TestContext.Current.CancellationToken)).ShouldBeFalse();
        }

        using (TenantContextSetter!.BeginScope(tenantB))
        {
            (await repository.WorkEmailTakenAsync("ali.kaya@example.com", null, TestContext.Current.CancellationToken)).ShouldBeFalse();
        }
    }
}

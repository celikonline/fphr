using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Employee.Tests;

/// <summary>
/// Integration tests for IEmployeeProvisioning (Faz 9 tur 1 ACİL KARAR, S-23):
/// creating employee records from candidates (ATS to HR core bridge). Once Employee.Domain adds
/// a SourceApplicationId field (migration required, ADR-0022 exemption), the idempotency test
/// can be fully implemented.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class EmployeeProvisioningTests : TestFixture
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        services.AddModuleDbContext<EmployeeDbContext>(configuration, EmployeeDbContext.SchemaName);
    }

    [Fact]
    public async Task CreateFromCandidate_WithNationalId_CreatesActiveEmployee_AndEnqueuesEmployeeHired()
    {
        var db = ServiceProvider!.GetRequiredService<EmployeeDbContext>();
        await db.Database.MigrateAsync();

        var tenantId = Guid.NewGuid();
        using var scope = TenantContextSetter!.BeginScope(tenantId);

        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);

        var repo = new EmployeeRepository(db);
        var unitOfWork = new EmployeeUnitOfWork(db);
        var provisioning = new EmployeeProvisioning(tenant, repo, outbox, unitOfWork);

        var request = new CandidateHireRequest(
            FirstName: "Ahmet",
            LastName: "Yılmaz",
            Email: "ahmet@example.com",
            Phone: "5551234567",
            NationalId: "12345678901",
            OrgUnitId: null,
            PositionTitle: "Software Engineer",
            HireDate: new DateOnly(2026, 9, 17),
            SourceApplicationId: Guid.NewGuid());

        var employeeId = await provisioning.CreateFromCandidateAsync(tenantId, request, CancellationToken.None);

        employeeId.ShouldNotBe(Guid.Empty);

        var employee = await db.Employees.FindAsync(employeeId);
        employee.ShouldNotBeNull();
        employee.FirstName.ShouldBe("Ahmet");
        employee.LastName.ShouldBe("Yılmaz");
        employee.NationalId.ShouldBe("12345678901");
        employee.WorkEmail.ShouldBe("ahmet@example.com");
        employee.Status.ShouldBe(EmployeeStatus.Active);
        employee.HireDate.ShouldBe(new DateOnly(2026, 9, 17));

        // Verify EmployeeHired event was enqueued (because NationalId was present and hire date was set).
        outbox.Received(1).Enqueue(Arg.Is<Contracts.Events.EmployeeHired>(
            e => e.EmployeeId == employeeId && e.TenantId == tenantId && e.HireDate == new DateOnly(2026, 9, 17)));
    }

    [Fact]
    public async Task CreateFromCandidate_WithoutNationalId_CreatesDraftEmployee_NoEmployeeHiredEvent()
    {
        var db = ServiceProvider!.GetRequiredService<EmployeeDbContext>();
        await db.Database.MigrateAsync();

        var tenantId = Guid.NewGuid();
        using var scope = TenantContextSetter!.BeginScope(tenantId);

        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);

        var repo = new EmployeeRepository(db);
        var unitOfWork = new EmployeeUnitOfWork(db);
        var provisioning = new EmployeeProvisioning(tenant, repo, outbox, unitOfWork);

        var request = new CandidateHireRequest(
            FirstName: "Fatima",
            LastName: "Kaya",
            Email: "fatima@example.com",
            Phone: null,
            NationalId: null,  // No TCKN
            OrgUnitId: null,
            PositionTitle: "Intern",
            HireDate: new DateOnly(2026, 9, 20),
            SourceApplicationId: Guid.NewGuid());

        var employeeId = await provisioning.CreateFromCandidateAsync(tenantId, request, CancellationToken.None);

        var employee = await db.Employees.FindAsync(employeeId);
        employee.ShouldNotBeNull();
        employee.FirstName.ShouldBe("Fatima");
        employee.LastName.ShouldBe("Kaya");
        employee.NationalId.ShouldBeNull();
        employee.WorkEmail.ShouldBe("fatima@example.com");
        employee.Status.ShouldBe(EmployeeStatus.Draft);
        employee.HireDate.ShouldBeNull();  // Not set, so stays Draft.

        // No EmployeeHired event because NationalId was missing (employee stayed Draft).
        outbox.DidNotReceive().Enqueue(Arg.Any<Contracts.Events.EmployeeHired>());
    }

    [Fact]
    public async Task CreateFromCandidate_TenantIsolation_EmployeesNotVisibleAcrossTenants()
    {
        var db = ServiceProvider!.GetRequiredService<EmployeeDbContext>();
        await db.Database.MigrateAsync();

        var tenantId1 = Guid.NewGuid();
        var tenantId2 = Guid.NewGuid();

        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var tenant1 = Substitute.For<ITenantContext>();
        tenant1.TenantId.Returns(tenantId1);
        var tenant2 = Substitute.For<ITenantContext>();
        tenant2.TenantId.Returns(tenantId2);

        var repo = new EmployeeRepository(db);

        Guid employee1Id;
        using (TenantContextSetter!.BeginScope(tenantId1))
        {
            var unitOfWork1 = new EmployeeUnitOfWork(db);
            var provisioning1 = new EmployeeProvisioning(tenant1, repo, outbox, unitOfWork1);
            var request1 = new CandidateHireRequest(
                FirstName: "Ali",
                LastName: "Demir",
                Email: "ali@tenant1.com",
                Phone: null,
                NationalId: "11111111111",
                OrgUnitId: null,
                PositionTitle: "Manager",
                HireDate: new DateOnly(2026, 9, 17),
                SourceApplicationId: Guid.NewGuid());
            employee1Id = await provisioning1.CreateFromCandidateAsync(tenantId1, request1, CancellationToken.None);
        }

        Guid employee2Id;
        using (TenantContextSetter!.BeginScope(tenantId2))
        {
            var unitOfWork2 = new EmployeeUnitOfWork(db);
            var provisioning2 = new EmployeeProvisioning(tenant2, repo, outbox, unitOfWork2);
            var request2 = new CandidateHireRequest(
                FirstName: "Zeynep",
                LastName: "Çetin",
                Email: "zeynep@tenant2.com",
                Phone: null,
                NationalId: "22222222222",
                OrgUnitId: null,
                PositionTitle: "Lead",
                HireDate: new DateOnly(2026, 9, 18),
                SourceApplicationId: Guid.NewGuid());
            employee2Id = await provisioning2.CreateFromCandidateAsync(tenantId2, request2, CancellationToken.None);
        }

        // Verify tenant 1 only sees its employee
        using (TenantContextSetter!.BeginScope(tenantId1))
        {
            var allEmployees = db.Employees.Where(e => e.TenantId == tenantId1).ToList();
            allEmployees.Count.ShouldBe(1);
            allEmployees[0].Id.ShouldBe(employee1Id);
            allEmployees[0].FirstName.ShouldBe("Ali");
        }

        // Verify tenant 2 only sees its employee
        using (TenantContextSetter!.BeginScope(tenantId2))
        {
            var allEmployees = db.Employees.Where(e => e.TenantId == tenantId2).ToList();
            allEmployees.Count.ShouldBe(1);
            allEmployees[0].Id.ShouldBe(employee2Id);
            allEmployees[0].FirstName.ShouldBe("Zeynep");
        }
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Payroll.Application.PayItems;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.PayItems;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Infrastructure.Persistence;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests.PayItems;

/// <summary>
/// Real PostgreSQL (shared test server, see TestFixture): the AddPayItems migration applies cleanly (Migrate throws on
/// pending model changes), the unique code index and tenant isolation hold, and the handlers persist assignments and
/// dirty flags through the real repositories.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class PayItemIntegrationTests : TestFixture
{
    private static readonly Guid EmployeeA = Guid.NewGuid();
    private static readonly Guid EmployeeB = Guid.NewGuid();

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;

        services.AddModuleDbContext<PayrollDbContext>(configuration, PayrollDbContext.SchemaName);
        services.AddScoped<IPayrollPeriodRepository, PayrollPeriodRepository>();
        services.AddScoped<IPayslipDirtyFlagRepository, PayslipDirtyFlagRepository>();
        services.AddScoped<IPayItemDefinitionRepository, PayItemDefinitionRepository>();
        services.AddScoped<IEmployeePayItemAssignmentRepository, EmployeePayItemAssignmentRepository>();
        services.AddScoped<PayslipDirtyFlagService>();
        services.AddScoped<PayItemChangeFlagger>();
        services.AddSingleton(Substitute.For<IEmployeeDirectory>());
    }

    protected override async Task OnDatabaseReadyAsync()
    {
        await ServiceProvider!.GetRequiredService<PayrollDbContext>().Database.MigrateAsync();

        var directory = ServiceProvider!.GetRequiredService<IEmployeeDirectory>();
        directory.GetContactsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<EmployeeContact>>(
                call.ArgAt<IReadOnlyCollection<Guid>>(1).Select(id => new EmployeeContact(id, "Ayşe", "Yılmaz", null, null, null, "Active")).ToList()));
    }

    private static PayItemDefinitionData Data(string formula = "deger") =>
        new("Yemek yardımı", PayItemDirection.Earning, PayItemAmountBasis.Gross, true, false, true, formula, 250.5m, new DateOnly(2026, 1, 1), null);

    private async Task<Guid> CreateDefinitionAsync(Guid tenantId, string code)
    {
        using var scope = ServiceProvider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
        var definition = PayItemDefinition.Create(tenantId, code, Data()).Value;
        scope.ServiceProvider.GetRequiredService<IPayItemDefinitionRepository>().Add(definition);
        await db.SaveChangesAsync();
        return definition.Id;
    }

    [Fact]
    public async Task Definition_RoundTrips_WithAllFields()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var id = await CreateDefinitionAsync(tenantId, "YEMEK");

        using var scope = ServiceProvider!.CreateScope();
        var loaded = (await scope.ServiceProvider.GetRequiredService<IPayItemDefinitionRepository>().FindByIdAsync(id))!;

        loaded.Code.ShouldBe("YEMEK");
        loaded.Name.ShouldBe("Yemek yardımı");
        loaded.Direction.ShouldBe(PayItemDirection.Earning);
        loaded.AmountBasis.ShouldBe(PayItemAmountBasis.Gross);
        (loaded.IsSgkSubject, loaded.IsIncomeTaxSubject, loaded.IsStampTaxSubject).ShouldBe((true, false, true));
        loaded.Formula.ShouldBe("deger");
        loaded.DefaultValue.ShouldBe(250.5m);
        loaded.ValidFrom.ShouldBe(new DateOnly(2026, 1, 1));
        loaded.ValidTo.ShouldBeNull();
        loaded.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task Code_IsUniquePerTenant_ButMayRepeatInAnotherTenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        using (TenantContextSetter!.BeginScope(tenantA))
        {
            await CreateDefinitionAsync(tenantA, "YEMEK");
            await Should.ThrowAsync<DbUpdateException>(() => CreateDefinitionAsync(tenantA, "YEMEK"));
        }

        using (TenantContextSetter.BeginScope(tenantB))
        {
            await Should.NotThrowAsync(() => CreateDefinitionAsync(tenantB, "YEMEK"));
        }
    }

    [Fact]
    public async Task Definitions_AreInvisibleToOtherTenants()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        Guid idA;
        using (TenantContextSetter!.BeginScope(tenantA))
        {
            idA = await CreateDefinitionAsync(tenantA, "YEMEK");
        }

        using (TenantContextSetter.BeginScope(tenantB))
        {
            using var scope = ServiceProvider!.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IPayItemDefinitionRepository>();

            (await repository.FindByIdAsync(idA)).ShouldBeNull();
            (await repository.CodeExistsAsync("YEMEK")).ShouldBeFalse();
            (await repository.ListAsync(includeInactive: true)).ShouldBeEmpty();
            (await repository.ListByIdsAsync([idA])).ShouldBeEmpty();
        }

        using (TenantContextSetter.BeginScope(tenantA))
        {
            using var scope = ServiceProvider!.CreateScope();
            (await scope.ServiceProvider.GetRequiredService<IPayItemDefinitionRepository>().ListAsync(includeInactive: false)).Select(d => d.Code).ShouldBe(["YEMEK"]);
        }
    }

    [Fact]
    public async Task Assignments_AreInvisibleToOtherTenants_AndFilterByEmployeeAndMonth()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        Guid definitionId;
        using (TenantContextSetter!.BeginScope(tenantA))
        {
            definitionId = await CreateDefinitionAsync(tenantA, "YEMEK");
            using var scope = ServiceProvider!.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
            var definition = (await scope.ServiceProvider.GetRequiredService<IPayItemDefinitionRepository>().FindByIdAsync(definitionId))!;
            var repository = scope.ServiceProvider.GetRequiredService<IEmployeePayItemAssignmentRepository>();
            var none = new HashSet<DateOnly>();
            repository.Add(EmployeePayItemAssignment.Create(tenantA, EmployeeA, definition, 100m, new DateOnly(2026, 3, 1), new DateOnly(2026, 5, 1), none).Value);
            repository.Add(EmployeePayItemAssignment.Create(tenantA, EmployeeB, definition, null, new DateOnly(2026, 6, 1), null, none).Value);
            await db.SaveChangesAsync();
        }

        using (TenantContextSetter.BeginScope(tenantA))
        {
            using var scope = ServiceProvider!.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IEmployeePayItemAssignmentRepository>();

            (await repository.ListByEmployeeAsync(EmployeeA)).Single().Value.ShouldBe(100m);
            (await repository.ListCoveringMonthAsync(new DateOnly(2026, 4, 1))).Select(a => a.EmployeeId).ShouldBe([EmployeeA]);
            (await repository.ListCoveringMonthAsync(new DateOnly(2026, 8, 1))).Select(a => a.EmployeeId).ShouldBe([EmployeeB]);
            (await repository.ListByDefinitionAsync(definitionId, new DateOnly(2026, 6, 1))).Select(a => a.EmployeeId).ShouldBe([EmployeeB]);
            (await repository.ListByEmployeesAsync([EmployeeA, EmployeeB])).Count.ShouldBe(2);
        }

        using (TenantContextSetter.BeginScope(tenantB))
        {
            using var scope = ServiceProvider!.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IEmployeePayItemAssignmentRepository>();

            (await repository.ListByEmployeeAsync(EmployeeA)).ShouldBeEmpty();
            (await repository.ListCoveringMonthAsync(new DateOnly(2026, 4, 1))).ShouldBeEmpty();
            (await repository.ListByEmployeesAsync([EmployeeA, EmployeeB])).ShouldBeEmpty();
            (await repository.ListByDefinitionAsync(definitionId, new DateOnly(2000, 1, 1))).ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task AssignHandler_PersistsAssignments_AndFlagsOnlyTheAssignedEmployeesInOpenPeriods()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var definitionId = await CreateDefinitionAsync(tenantId, "YEMEK");
        Guid openPeriodId;
        using (var scope = ServiceProvider!.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
            var open = PayrollPeriod.Create(tenantId, 2026, 6).Value;
            var approved = PayrollPeriod.Create(tenantId, 2026, 5).Value;
            approved.Approve(null);
            db.Periods.AddRange(open, approved);
            await db.SaveChangesAsync();
            openPeriodId = open.Id;
        }

        using (var scope = ServiceProvider!.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var handler = new CreatePayItemAssignmentsHandler(
                sp.GetRequiredService<ITenantContext>(), sp.GetRequiredService<IPayrollPeriodRepository>(), sp.GetRequiredService<IPayItemDefinitionRepository>(),
                sp.GetRequiredService<IEmployeePayItemAssignmentRepository>(), sp.GetRequiredService<IEmployeeDirectory>(), sp.GetRequiredService<PayItemChangeFlagger>());

            // May is closed: refused, nothing written.
            (await handler.Handle(new CreatePayItemAssignmentsCommand(definitionId, [EmployeeA], 1m, 2026, 5, null, null), CancellationToken.None))
                .Error.Code.ShouldBe(PayrollDomainErrors.PayItemAssignmentPeriodClosed);

            var result = await handler.Handle(new CreatePayItemAssignmentsCommand(definitionId, [EmployeeA, EmployeeB], 750m, 2026, 6, 2026, 6), CancellationToken.None);
            result.IsSuccess.ShouldBeTrue();
            await sp.GetRequiredService<PayrollDbContext>().SaveChangesAsync();
        }

        using (var scope = ServiceProvider!.CreateScope())
        {
            var sp = scope.ServiceProvider;
            (await sp.GetRequiredService<IEmployeePayItemAssignmentRepository>().ListCoveringMonthAsync(new DateOnly(2026, 6, 1))).Count.ShouldBe(2);
            var flags = await sp.GetRequiredService<IPayslipDirtyFlagRepository>().ListActiveByPeriodAsync(openPeriodId);
            flags.Select(f => f.EmployeeId).ShouldBe([EmployeeA, EmployeeB], ignoreOrder: true);
            flags.ShouldAllBe(f => f.Reasons.Contains(PayslipDirtyFlagReasons.PayItemChanged));
        }
    }

    [Fact]
    public async Task DefinitionChange_FlagsEveryEmployeeAssignedInAnOpenPeriod_ButNotEndedOnes()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var definitionId = await CreateDefinitionAsync(tenantId, "YEMEK");
        Guid openPeriodId;
        using (var scope = ServiceProvider!.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
            var open = PayrollPeriod.Create(tenantId, 2026, 6).Value;
            db.Periods.Add(open);
            var definition = (await scope.ServiceProvider.GetRequiredService<IPayItemDefinitionRepository>().FindByIdAsync(definitionId))!;
            var none = new HashSet<DateOnly>();
            db.PayItemAssignments.Add(EmployeePayItemAssignment.Create(tenantId, EmployeeA, definition, 1m, new DateOnly(2026, 1, 1), null, none).Value);
            db.PayItemAssignments.Add(EmployeePayItemAssignment.Create(tenantId, EmployeeB, definition, 1m, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1), none).Value);
            await db.SaveChangesAsync();
            openPeriodId = open.Id;
        }

        using (var scope = ServiceProvider!.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var result = await new DeactivatePayItemHandler(sp.GetRequiredService<IPayItemDefinitionRepository>(), sp.GetRequiredService<PayItemChangeFlagger>())
                .Handle(new DeactivatePayItemCommand(definitionId), CancellationToken.None);
            result.IsSuccess.ShouldBeTrue();
            await sp.GetRequiredService<PayrollDbContext>().SaveChangesAsync();
        }

        using (var scope = ServiceProvider!.CreateScope())
        {
            var flags = await scope.ServiceProvider.GetRequiredService<IPayslipDirtyFlagRepository>().ListActiveByPeriodAsync(openPeriodId);
            flags.Select(f => f.EmployeeId).ShouldBe([EmployeeA]);
            (await scope.ServiceProvider.GetRequiredService<IPayItemDefinitionRepository>().FindByIdAsync(definitionId))!.IsActive.ShouldBeFalse();
        }
    }

    [Fact]
    public async Task OverlapRule_RunsAgainstPersistedAssignments()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var definitionId = await CreateDefinitionAsync(tenantId, "YEMEK");
        var otherId = await CreateDefinitionAsync(tenantId, "DIGER");

        async Task<Sense.Shared.Kernel.Results.Result<CreatePayItemAssignmentsResult>> AssignAsync(Guid defId, int fromMonth, int? toMonth)
        {
            using var scope = ServiceProvider!.CreateScope();
            var sp = scope.ServiceProvider;
            var handler = new CreatePayItemAssignmentsHandler(
                sp.GetRequiredService<ITenantContext>(), sp.GetRequiredService<IPayrollPeriodRepository>(), sp.GetRequiredService<IPayItemDefinitionRepository>(),
                sp.GetRequiredService<IEmployeePayItemAssignmentRepository>(), sp.GetRequiredService<IEmployeeDirectory>(), sp.GetRequiredService<PayItemChangeFlagger>());
            var result = await handler.Handle(new CreatePayItemAssignmentsCommand(defId, [EmployeeA], 1m, 2026, fromMonth, toMonth is null ? null : 2026, toMonth), CancellationToken.None);
            await sp.GetRequiredService<PayrollDbContext>().SaveChangesAsync();
            return result;
        }

        (await AssignAsync(definitionId, 3, 5)).IsSuccess.ShouldBeTrue();
        (await AssignAsync(definitionId, 5, 6)).Error.Code.ShouldBe(PayrollDomainErrors.PayItemAssignmentOverlap);
        (await AssignAsync(otherId, 3, 5)).IsSuccess.ShouldBeTrue();
        (await AssignAsync(definitionId, 6, 7)).IsSuccess.ShouldBeTrue();
        (await AssignAsync(definitionId, 1, 3)).Error.Code.ShouldBe(PayrollDomainErrors.PayItemAssignmentOverlap);
    }
}

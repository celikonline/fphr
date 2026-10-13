using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Payroll.Application.Incentives;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Incentives;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Modules.Payroll.Domain.Profiles;
using Sense.Modules.Payroll.Infrastructure.Persistence;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests.Incentives;

/// <summary>
/// Real PostgreSQL (shared test server, see TestFixture): the AddIncentives migration applies cleanly (Migrate throws on
/// pending model changes), the jsonb parameters column round-trips, the unique code and partial template indexes hold,
/// tenants are isolated, and the handlers persist assignments and dirty flags through the real repositories.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class IncentiveIntegrationTests : TestFixture
{
    private static readonly Guid EmployeeA = Guid.NewGuid();
    private static readonly Guid EmployeeB = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly Jan = new(2026, 1, 1);

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;

        services.AddModuleDbContext<PayrollDbContext>(configuration, PayrollDbContext.SchemaName);
        services.AddScoped<IPayrollPeriodRepository, PayrollPeriodRepository>();
        services.AddScoped<IPayslipDirtyFlagRepository, PayslipDirtyFlagRepository>();
        services.AddScoped<IEmployeePayrollProfileRepository, EmployeePayrollProfileRepository>();
        services.AddScoped<IIncentiveDefinitionRepository, IncentiveDefinitionRepository>();
        services.AddScoped<IEmployeeIncentiveAssignmentRepository, EmployeeIncentiveAssignmentRepository>();
        services.AddScoped<PayslipDirtyFlagService>();
        services.AddScoped<IncentiveChangeFlagger>();
        services.AddSingleton(Substitute.For<IEmployeeDirectory>());
        services.AddSingleton(TimeProvider.System);
    }

    protected override async Task OnDatabaseReadyAsync()
    {
        await ServiceProvider!.GetRequiredService<PayrollDbContext>().Database.MigrateAsync();

        var directory = ServiceProvider!.GetRequiredService<IEmployeeDirectory>();
        directory.GetContactsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<EmployeeContact>>(
                call.ArgAt<IReadOnlyCollection<Guid>>(1).Select(id => new EmployeeContact(id, "Ayşe", "Yılmaz", null, null, null, "Active")).ToList()));
    }

    private static IncentiveParameters CoverageParameters() => new()
    {
        Rate = IncentiveRate.Key("Payroll.Sgk.EmployerRate"),
        CoverageBase = IncentiveCoverageBase.MinimumWage,
        CapMultiplier = 1.5m,
        EmployerSgkRate = IncentiveRate.Literal(0.155m),
        CoversEmployerSgk = true,
        CoversEmployerUnemployment = true,
        CoversEmployeeSgk = false,
        CoversEmployeeUnemployment = true,
    };

    private static IncentiveDefinitionData Data(IncentiveKind kind = IncentiveKind.EmployerSgkRateDiscount, IncentiveParameters? parameters = null, string? declarationCode = "06111") =>
        new("Test teşviki", "Test Kanunu", declarationCode, kind, parameters ?? IncentiveDefinitionTests.DefaultParameters(kind), null, Jan, null);

    private async Task<Guid> CreateDefinitionAsync(
        Guid tenantId, string code, IncentiveKind kind = IncentiveKind.EmployerSgkRateDiscount, IncentiveParameters? parameters = null, string? templateKey = null, string? declarationCode = "06111")
    {
        using var scope = ServiceProvider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
        var definition = IncentiveDefinition.Create(tenantId, code, Data(kind, parameters, declarationCode), templateKey).Value;
        scope.ServiceProvider.GetRequiredService<IIncentiveDefinitionRepository>().Add(definition);
        await db.SaveChangesAsync();
        return definition.Id;
    }

    [Fact]
    public async Task CoverageDefinition_RoundTripsThroughTheJsonbParametersColumn()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var id = await CreateDefinitionAsync(tenantId, "KAPSAMA", IncentiveKind.EmployerShareCoverage, CoverageParameters());

        using var scope = ServiceProvider!.CreateScope();
        var loaded = (await scope.ServiceProvider.GetRequiredService<IIncentiveDefinitionRepository>().FindByIdAsync(id))!;

        loaded.Code.ShouldBe("KAPSAMA");
        loaded.Kind.ShouldBe(IncentiveKind.EmployerShareCoverage);
        loaded.Category.ShouldBe(IncentiveCategory.Sgk);
        loaded.DeclarationCode.ShouldBe("06111");
        loaded.IsActive.ShouldBeTrue();
        loaded.IsSmmmApproved.ShouldBeFalse();
        loaded.Parameters.ShouldBe(CoverageParameters());
        loaded.Parameters.Rate!.LegislationKey.ShouldBe("Payroll.Sgk.EmployerRate");
        loaded.Parameters.Rate.Value.ShouldBeNull();
        loaded.Parameters.CoverageBase.ShouldBe(IncentiveCoverageBase.MinimumWage);
        loaded.Parameters.CapMultiplier.ShouldBe(1.5m);
        loaded.Parameters.EmployerSgkRate!.Value.ShouldBe(0.155m);
        (loaded.Parameters.CoversEmployerSgk, loaded.Parameters.CoversEmployerUnemployment, loaded.Parameters.CoversEmployeeSgk, loaded.Parameters.CoversEmployeeUnemployment)
            .ShouldBe((true, true, false, true));
    }

    [Fact]
    public async Task Parameters_AreStoredAsCamelCaseJsonb_InTheSameShapeAsTheApi()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var id = await CreateDefinitionAsync(tenantId, "KAPSAMA", IncentiveKind.EmployerShareCoverage, CoverageParameters());

        using var scope = ServiceProvider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
        var json = await db.Database
            .SqlQuery<string>($"select parameters::text as \"Value\" from payroll.payroll_incentive_definitions where id = {id}")
            .SingleAsync();

        using var document = System.Text.Json.JsonDocument.Parse(json);
        var root = document.RootElement;
        root.GetProperty("rate").GetProperty("legislationKey").GetString().ShouldBe("Payroll.Sgk.EmployerRate");
        root.GetProperty("coverageBase").GetString().ShouldBe("minimumWage");
        root.GetProperty("capMultiplier").GetDecimal().ShouldBe(1.5m);
        root.GetProperty("employerSgkRate").GetProperty("value").GetDecimal().ShouldBe(0.155m);
        root.GetProperty("coversEmployerSgk").GetBoolean().ShouldBeTrue();
        root.GetProperty("coversEmployeeSgk").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task DraftWithoutParameters_AndTaxAndDeclarationOnlyKinds_RoundTrip()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var draftId = await CreateDefinitionAsync(tenantId, "TASLAK", IncentiveKind.EmployerShareCoverage, IncentiveParameters.Empty, declarationCode: null);
        var taxId = await CreateDefinitionAsync(tenantId, "ARGE_GV", IncentiveKind.IncomeTaxWithholdingRelief, new IncentiveParameters { Rate = IncentiveRate.Literal(0.8m) }, declarationCode: null);
        var onlyId = await CreateDefinitionAsync(tenantId, "SADECE", IncentiveKind.DeclarationOnly, IncentiveParameters.Empty);

        using var scope = ServiceProvider!.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IIncentiveDefinitionRepository>();
        var draft = (await repository.FindByIdAsync(draftId))!;
        var tax = (await repository.FindByIdAsync(taxId))!;
        var only = (await repository.FindByIdAsync(onlyId))!;

        draft.Parameters.ShouldBe(IncentiveParameters.Empty);
        draft.IsComplete.ShouldBeFalse();
        tax.Category.ShouldBe(IncentiveCategory.Tax);
        tax.Parameters.Rate!.Value.ShouldBe(0.8m);
        tax.Parameters.CoverageBase.ShouldBeNull();
        only.Parameters.ShouldBe(IncentiveParameters.Empty);
        only.Kind.ShouldBe(IncentiveKind.DeclarationOnly);
    }

    [Fact]
    public async Task Update_ReplacesTheParameters_AndPersistsTheDroppedApproval()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var id = await CreateDefinitionAsync(tenantId, "SGK_A");
        using (var scope = ServiceProvider!.CreateScope())
        {
            var definition = (await scope.ServiceProvider.GetRequiredService<IIncentiveDefinitionRepository>().FindByIdAsync(id))!;
            definition.Approve(UserId, DateTimeOffset.UtcNow, "ilk onay").IsSuccess.ShouldBeTrue();
            await scope.ServiceProvider.GetRequiredService<PayrollDbContext>().SaveChangesAsync();
        }

        using (var scope = ServiceProvider!.CreateScope())
        {
            var definition = (await scope.ServiceProvider.GetRequiredService<IIncentiveDefinitionRepository>().FindByIdAsync(id))!;
            definition.IsSmmmApproved.ShouldBeTrue();
            definition.ApprovedByUserId.ShouldBe(UserId);
            definition.ApprovalNote.ShouldBe("ilk onay");
            definition.Update(Data(parameters: new IncentiveParameters { Rate = IncentiveRate.Key("Payroll.Sgk.EmployerDiscountRate") })).IsSuccess.ShouldBeTrue();
            await scope.ServiceProvider.GetRequiredService<PayrollDbContext>().SaveChangesAsync();
        }

        using (var scope = ServiceProvider!.CreateScope())
        {
            var definition = (await scope.ServiceProvider.GetRequiredService<IIncentiveDefinitionRepository>().FindByIdAsync(id))!;
            definition.Parameters.Rate!.LegislationKey.ShouldBe("Payroll.Sgk.EmployerDiscountRate");
            definition.Parameters.Rate.Value.ShouldBeNull();
            definition.IsSmmmApproved.ShouldBeFalse();
            definition.ApprovedAt.ShouldBeNull();
            definition.ApprovedByUserId.ShouldBeNull();
        }
    }

    [Fact]
    public async Task Code_IsUniquePerTenant_ButMayRepeatInAnotherTenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        using (TenantContextSetter!.BeginScope(tenantA))
        {
            await CreateDefinitionAsync(tenantA, "SGK_A");
            await Should.ThrowAsync<DbUpdateException>(() => CreateDefinitionAsync(tenantA, "SGK_A"));
        }

        using (TenantContextSetter.BeginScope(tenantB))
        {
            await Should.NotThrowAsync(() => CreateDefinitionAsync(tenantB, "SGK_A"));
        }
    }

    [Fact]
    public async Task TemplateKey_IsUniquePerTenantOnlyWhenSet()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        using (TenantContextSetter!.BeginScope(tenantA))
        {
            // Many definitions without a template key are fine (partial index).
            await CreateDefinitionAsync(tenantA, "ELLE_A");
            await CreateDefinitionAsync(tenantA, "ELLE_B");
            await CreateDefinitionAsync(tenantA, "SGK_5510_MD81", templateKey: IncentiveTemplates.Tr5510Md81, declarationCode: "05510");
            await Should.ThrowAsync<DbUpdateException>(() => CreateDefinitionAsync(tenantA, "BASKA_KOD", templateKey: IncentiveTemplates.Tr5510Md81));

            using var scope = ServiceProvider!.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IIncentiveDefinitionRepository>();
            (await repository.TemplateExistsAsync(IncentiveTemplates.Tr5510Md81)).ShouldBeTrue();
            (await repository.TemplateExistsAsync(IncentiveTemplates.Tr6111)).ShouldBeFalse();
        }

        using (TenantContextSetter.BeginScope(tenantB))
        {
            await Should.NotThrowAsync(() => CreateDefinitionAsync(tenantB, "SGK_5510_MD81", templateKey: IncentiveTemplates.Tr5510Md81, declarationCode: "05510"));
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
            idA = await CreateDefinitionAsync(tenantA, "SGK_A", templateKey: IncentiveTemplates.Tr5510Md81);
        }

        using (TenantContextSetter.BeginScope(tenantB))
        {
            using var scope = ServiceProvider!.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IIncentiveDefinitionRepository>();

            (await repository.FindByIdAsync(idA)).ShouldBeNull();
            (await repository.CodeExistsAsync("SGK_A")).ShouldBeFalse();
            (await repository.TemplateExistsAsync(IncentiveTemplates.Tr5510Md81)).ShouldBeFalse();
            (await repository.ListAsync(includeInactive: true)).ShouldBeEmpty();
            (await repository.ListByIdsAsync([idA])).ShouldBeEmpty();
        }

        using (TenantContextSetter.BeginScope(tenantA))
        {
            using var scope = ServiceProvider!.CreateScope();
            (await scope.ServiceProvider.GetRequiredService<IIncentiveDefinitionRepository>().ListAsync(includeInactive: false)).Select(d => d.Code).ShouldBe(["SGK_A"]);
        }
    }

    [Fact]
    public async Task Assignments_AreInvisibleToOtherTenants_AndFilterByEmployeeMonthAndDefinition()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        Guid definitionId;
        using (TenantContextSetter!.BeginScope(tenantA))
        {
            definitionId = await CreateDefinitionAsync(tenantA, "SGK_A");
            using var scope = ServiceProvider!.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
            var definition = (await scope.ServiceProvider.GetRequiredService<IIncentiveDefinitionRepository>().FindByIdAsync(definitionId))!;
            var repository = scope.ServiceProvider.GetRequiredService<IEmployeeIncentiveAssignmentRepository>();
            var none = new HashSet<DateOnly>();
            repository.Add(EmployeeIncentiveAssignment.Create(tenantA, EmployeeA, definition, new DateOnly(2026, 3, 1), new DateOnly(2026, 5, 1), "not", none, [], _ => IncentiveCategory.Sgk).Value);
            repository.Add(EmployeeIncentiveAssignment.Create(tenantA, EmployeeB, definition, new DateOnly(2026, 6, 1), null, null, none, [], _ => IncentiveCategory.Sgk).Value);
            await db.SaveChangesAsync();
        }

        using (TenantContextSetter.BeginScope(tenantA))
        {
            using var scope = ServiceProvider!.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IEmployeeIncentiveAssignmentRepository>();

            var forA = (await repository.ListByEmployeeAsync(EmployeeA)).Single();
            forA.Note.ShouldBe("not");
            forA.ValidFromMonth.ShouldBe(new DateOnly(2026, 3, 1));
            forA.ValidToMonth.ShouldBe(new DateOnly(2026, 5, 1));
            (await repository.ListCoveringMonthAsync(new DateOnly(2026, 4, 1))).Select(a => a.EmployeeId).ShouldBe([EmployeeA]);
            (await repository.ListCoveringMonthAsync(new DateOnly(2026, 8, 1))).Select(a => a.EmployeeId).ShouldBe([EmployeeB]);
            (await repository.ListByDefinitionAsync(definitionId, new DateOnly(2026, 6, 1))).Select(a => a.EmployeeId).ShouldBe([EmployeeB]);
            (await repository.ListByEmployeesAsync([EmployeeA, EmployeeB])).Count.ShouldBe(2);
        }

        using (TenantContextSetter.BeginScope(tenantB))
        {
            using var scope = ServiceProvider!.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IEmployeeIncentiveAssignmentRepository>();

            (await repository.ListByEmployeeAsync(EmployeeA)).ShouldBeEmpty();
            (await repository.ListCoveringMonthAsync(new DateOnly(2026, 4, 1))).ShouldBeEmpty();
            (await repository.ListByEmployeesAsync([EmployeeA, EmployeeB])).ShouldBeEmpty();
            (await repository.ListByDefinitionAsync(definitionId, new DateOnly(2000, 1, 1))).ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task ADefinitionWithAssignments_CannotBeDeleted()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var definitionId = await CreateDefinitionAsync(tenantId, "SGK_A");
        using (var scope = ServiceProvider!.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
            var definition = (await scope.ServiceProvider.GetRequiredService<IIncentiveDefinitionRepository>().FindByIdAsync(definitionId))!;
            db.IncentiveAssignments.Add(EmployeeIncentiveAssignment.Create(tenantId, EmployeeA, definition, Jan, null, null, new HashSet<DateOnly>(), [], _ => IncentiveCategory.Sgk).Value);
            await db.SaveChangesAsync();
        }

        using (var scope = ServiceProvider!.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
            db.IncentiveDefinitions.Remove((await db.IncentiveDefinitions.SingleAsync(d => d.Id == definitionId)));
            await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task TemplateHandler_PersistsThe5510TemplateApprovedBySystem()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        Guid id;
        using (var scope = ServiceProvider!.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var handler = new CreateIncentiveFromTemplateHandler(sp.GetRequiredService<ITenantContext>(), sp.GetRequiredService<IIncentiveDefinitionRepository>(), TimeProvider.System);
            var result = await handler.Handle(new CreateIncentiveFromTemplateCommand(IncentiveTemplates.Tr5510Md81), CancellationToken.None);
            result.IsSuccess.ShouldBeTrue();
            id = result.Value;
            await sp.GetRequiredService<PayrollDbContext>().SaveChangesAsync();
        }

        using (var scope = ServiceProvider!.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var stored = (await sp.GetRequiredService<IIncentiveDefinitionRepository>().FindByIdAsync(id))!;
            stored.IsSmmmApproved.ShouldBeTrue();
            stored.ApprovedByUserId.ShouldBeNull();
            stored.ApprovedAt.ShouldNotBeNull();
            stored.Parameters.Rate!.LegislationKey.ShouldBe("Payroll.Sgk.EmployerDiscountRate");

            var again = new CreateIncentiveFromTemplateHandler(sp.GetRequiredService<ITenantContext>(), sp.GetRequiredService<IIncentiveDefinitionRepository>(), TimeProvider.System);
            (await again.Handle(new CreateIncentiveFromTemplateCommand(IncentiveTemplates.Tr5510Md81), CancellationToken.None)).Error.Code
                .ShouldBe(PayrollDomainErrors.IncentiveTemplateAlreadyAdded);
        }
    }

    private async Task<Guid> OpenPeriodAsync(Guid tenantId, int year, int month, bool approved = false)
    {
        using var scope = ServiceProvider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
        var period = PayrollPeriod.Create(tenantId, year, month).Value;
        if (approved)
        {
            period.Approve(null);
        }

        db.Periods.Add(period);
        await db.SaveChangesAsync();
        return period.Id;
    }

    private async Task<Sense.Shared.Kernel.Results.Result<CreateIncentiveAssignmentsResult>> AssignAsync(
        Guid definitionId, Guid[] employees, int fromMonth, int? toMonth)
    {
        using var scope = ServiceProvider!.CreateScope();
        var sp = scope.ServiceProvider;
        var handler = new CreateIncentiveAssignmentsHandler(
            sp.GetRequiredService<ITenantContext>(), sp.GetRequiredService<IPayrollPeriodRepository>(), sp.GetRequiredService<IIncentiveDefinitionRepository>(),
            sp.GetRequiredService<IEmployeeIncentiveAssignmentRepository>(), sp.GetRequiredService<IEmployeeDirectory>(), sp.GetRequiredService<IncentiveChangeFlagger>());
        var result = await handler.Handle(
            new CreateIncentiveAssignmentsCommand(definitionId, employees, 2026, fromMonth, toMonth is null ? null : 2026, toMonth, null), CancellationToken.None);
        await sp.GetRequiredService<PayrollDbContext>().SaveChangesAsync();
        return result;
    }

    [Fact]
    public async Task AssignHandler_PersistsAssignments_FlagsOnlyOpenPeriods_AndEnforcesCategoryOverlap()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var sgkId = await CreateDefinitionAsync(tenantId, "SGK_A");
        var otherSgkId = await CreateDefinitionAsync(tenantId, "SGK_B", declarationCode: "07103");
        var taxId = await CreateDefinitionAsync(tenantId, "ARGE_GV", IncentiveKind.IncomeTaxWithholdingRelief, declarationCode: null);
        var openPeriodId = await OpenPeriodAsync(tenantId, 2026, 6);
        await OpenPeriodAsync(tenantId, 2026, 5, approved: true);

        // May is closed: refused, nothing written, nothing flagged.
        (await AssignAsync(sgkId, [EmployeeA], 5, null)).Error.Code.ShouldBe(PayrollDomainErrors.IncentiveAssignmentPeriodClosed);

        (await AssignAsync(sgkId, [EmployeeA, EmployeeB], 6, 8)).IsSuccess.ShouldBeTrue();
        // Same category, overlapping months, other definition: refused; a tax incentive alongside is fine.
        (await AssignAsync(otherSgkId, [EmployeeA], 8, 10)).Error.Code.ShouldBe(PayrollDomainErrors.IncentiveAssignmentOverlap);
        (await AssignAsync(otherSgkId, [EmployeeA], 9, 10)).IsSuccess.ShouldBeTrue();
        (await AssignAsync(taxId, [EmployeeA], 6, null)).IsSuccess.ShouldBeTrue();

        using var scope = ServiceProvider!.CreateScope();
        var sp = scope.ServiceProvider;
        (await sp.GetRequiredService<IEmployeeIncentiveAssignmentRepository>().ListByEmployeeAsync(EmployeeA)).Count.ShouldBe(3);
        (await sp.GetRequiredService<IEmployeeIncentiveAssignmentRepository>().ListCoveringMonthAsync(new DateOnly(2026, 6, 1))).Count.ShouldBe(3);
        var flags = await sp.GetRequiredService<IPayslipDirtyFlagRepository>().ListActiveByPeriodAsync(openPeriodId);
        flags.Select(f => f.EmployeeId).ShouldBe([EmployeeA, EmployeeB], ignoreOrder: true);
        flags.ShouldAllBe(f => f.Reasons.Contains(PayslipDirtyFlagReasons.IncentiveChanged));
    }

    [Fact]
    public async Task DefinitionChanges_FlagEveryEmployeeAssignedInAnOpenPeriod_ButNotEndedOnes()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var definitionId = await CreateDefinitionAsync(tenantId, "SGK_A");
        var openPeriodId = await OpenPeriodAsync(tenantId, 2026, 6);
        using (var scope = ServiceProvider!.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
            var definition = (await scope.ServiceProvider.GetRequiredService<IIncentiveDefinitionRepository>().FindByIdAsync(definitionId))!;
            var none = new HashSet<DateOnly>();
            db.IncentiveAssignments.Add(EmployeeIncentiveAssignment.Create(tenantId, EmployeeA, definition, Jan, null, null, none, [], _ => IncentiveCategory.Sgk).Value);
            db.IncentiveAssignments.Add(EmployeeIncentiveAssignment.Create(tenantId, EmployeeB, definition, Jan, new DateOnly(2026, 3, 1), null, none, [], _ => IncentiveCategory.Sgk).Value);
            await db.SaveChangesAsync();
        }

        using (var scope = ServiceProvider!.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var result = await new DeactivateIncentiveDefinitionHandler(sp.GetRequiredService<IIncentiveDefinitionRepository>(), sp.GetRequiredService<IncentiveChangeFlagger>())
                .Handle(new DeactivateIncentiveDefinitionCommand(definitionId), CancellationToken.None);
            result.IsSuccess.ShouldBeTrue();
            await sp.GetRequiredService<PayrollDbContext>().SaveChangesAsync();
        }

        using (var scope = ServiceProvider!.CreateScope())
        {
            var flags = await scope.ServiceProvider.GetRequiredService<IPayslipDirtyFlagRepository>().ListActiveByPeriodAsync(openPeriodId);
            flags.Select(f => f.EmployeeId).ShouldBe([EmployeeA]);
            flags.Single().Reasons.ShouldContain(PayslipDirtyFlagReasons.IncentiveChanged);
            (await scope.ServiceProvider.GetRequiredService<IIncentiveDefinitionRepository>().FindByIdAsync(definitionId))!.IsActive.ShouldBeFalse();
        }
    }

    [Fact]
    public async Task MonthPlan_OverRealRepositories_ReturnsTheEffectiveAssignmentsAndDefinitions()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var sgkId = await CreateDefinitionAsync(tenantId, "SGK_A");
        var taxId = await CreateDefinitionAsync(tenantId, "ARGE_GV", IncentiveKind.IncomeTaxWithholdingRelief, declarationCode: null);
        (await AssignAsync(sgkId, [EmployeeA, EmployeeB], 3, 9)).IsSuccess.ShouldBeTrue();
        (await AssignAsync(taxId, [EmployeeA], 1, null)).IsSuccess.ShouldBeTrue();

        using var scope = ServiceProvider!.CreateScope();
        var plan = await IncentiveMonthPlan.LoadAsync(
            new DateOnly(2026, 4, 1), scope.ServiceProvider.GetRequiredService<IEmployeeIncentiveAssignmentRepository>(), scope.ServiceProvider.GetRequiredService<IIncentiveDefinitionRepository>());

        plan.Definitions.Keys.ShouldBe([sgkId, taxId], ignoreOrder: true);
        plan.ForEmployee(EmployeeA).Select(p => p.Definition.Code).ShouldBe(["ARGE_GV", "SGK_A"], ignoreOrder: true);
        plan.ForEmployee(EmployeeB).Select(p => p.Definition.Code).ShouldBe(["SGK_A"]);

        var later = await IncentiveMonthPlan.LoadAsync(
            new DateOnly(2026, 11, 1), scope.ServiceProvider.GetRequiredService<IEmployeeIncentiveAssignmentRepository>(), scope.ServiceProvider.GetRequiredService<IIncentiveDefinitionRepository>());
        later.ForEmployee(EmployeeA).Select(p => p.Definition.Code).ShouldBe(["ARGE_GV"]);
        later.ForEmployee(EmployeeB).ShouldBeEmpty();
    }

    [Fact]
    public async Task LegacyConversion_OverRealRepositories_CreatesSkipsAndLeavesTheProfileCode()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var definitionId = await CreateDefinitionAsync(tenantId, "SGK_5510", declarationCode: "05510");
        var otherId = await CreateDefinitionAsync(tenantId, "SGK_6111");
        await OpenPeriodAsync(tenantId, 2026, 6);
        (await AssignAsync(otherId, [EmployeeB], 1, null)).IsSuccess.ShouldBeTrue();
        using (var scope = ServiceProvider!.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
            foreach (var employeeId in new[] { EmployeeA, EmployeeB })
            {
                db.EmployeePayrollProfiles.Add(EmployeePayrollProfile.Create(
                    tenantId, employeeId,
                    new EmployeePayrollProfileData(
                        Guid.NewGuid(), null, new DateOnly(2020, 1, 1), null, WageBasis.Monthly, SgkStatus.Normal, "5510", MealBenefitVariant.None, 0m,
                        TransportVariant.None, PayrollPaymentMethod.Bank, null, false, null, null)).Value);
            }

            await db.SaveChangesAsync();
        }

        ConvertLegacyIncentiveCodeResult converted;
        using (var scope = ServiceProvider!.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var handler = new ConvertLegacyIncentiveCodeHandler(
                sp.GetRequiredService<ITenantContext>(), sp.GetRequiredService<IEmployeePayrollProfileRepository>(), sp.GetRequiredService<IPayrollPeriodRepository>(),
                sp.GetRequiredService<IIncentiveDefinitionRepository>(), sp.GetRequiredService<IEmployeeIncentiveAssignmentRepository>(), sp.GetRequiredService<IncentiveChangeFlagger>(),
                TimeProvider.System);
            var result = await handler.Handle(new ConvertLegacyIncentiveCodeCommand("5510", definitionId, 2026, 6, null, null), CancellationToken.None);
            result.IsSuccess.ShouldBeTrue();
            converted = result.Value;
            await sp.GetRequiredService<PayrollDbContext>().SaveChangesAsync();
        }

        converted.CreatedIds.Count.ShouldBe(1);
        converted.SkippedEmployeeIds.ShouldBe([EmployeeB]);
        using (var scope = ServiceProvider!.CreateScope())
        {
            var sp = scope.ServiceProvider;
            (await sp.GetRequiredService<IEmployeeIncentiveAssignmentRepository>().ListByEmployeeAsync(EmployeeA)).Single().IncentiveDefinitionId.ShouldBe(definitionId);
            (await sp.GetRequiredService<IEmployeePayrollProfileRepository>().ListByEmployeeAsync(EmployeeA)).Single().IncentiveLawCode.ShouldBe("5510");
            var preview = (await new ListLegacyIncentiveCodesHandler(
                    sp.GetRequiredService<IEmployeePayrollProfileRepository>(), sp.GetRequiredService<IIncentiveDefinitionRepository>(), TimeProvider.System)
                .Handle(new ListLegacyIncentiveCodesQuery(), CancellationToken.None)).Value;
            preview.Single().NormalizedCode.ShouldBe("05510");
            preview.Single().EmployeeCount.ShouldBe(2);
            preview.Single().SuggestedDefinitionCode.ShouldBe("SGK_5510");
        }
    }
}

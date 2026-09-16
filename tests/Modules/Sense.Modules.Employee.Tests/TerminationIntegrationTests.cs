using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Assets.Contracts;
using Sense.Modules.Employee.Api;
using Sense.Modules.Employee.Application;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Application.Terminations;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
// Both Domain.Employees and Contracts.Events declare a TerminationType enum (Contracts stays independent of Domain,
// per architecture rule) - alias the Contracts one to keep bare TerminationType meaning the Domain enum here.
using ContractEvents = Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Organization.Api;
using Sense.Modules.Organization.Infrastructure.Persistence;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Legislation;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Context;
using Sense.Shared.Infrastructure.Persistence.Outbox;
using Sense.Shared.Kernel.Results;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Employee.Tests;

/// <summary>
/// Termination wizard (Faz 1a E5a, 11.3.03): TerminateEmployeeCommand end to end (reason catalog auto-seeded, defaults
/// applied, EmployeeTerminated via the outbox -&gt; OutboxProcessor -&gt; IEventBus) and the job-security scope/warning
/// matrix (JobSecurityEvaluator). ILegislationParameterReader is substituted here rather than wiring the real Leave
/// module - the seed values it stands in for are documented at each call site.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class TerminationIntegrationTests : TestFixture
{
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();
    private readonly ILegislationParameterReader _legislation = Substitute.For<ILegislationParameterReader>();
    private readonly IAssetLookup _assetLookup = Substitute.For<IAssetLookup>();
    private readonly RecordingHandler<ContractEvents.EmployeeTerminated> _terminated = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        new OrganizationModule().AddModule(services, configuration);
        new EmployeeModule().AddModule(services, configuration);

        _permissions.HasAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        services.AddSingleton(_permissions);

        _dataScope.CanAccessEmployeeAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        _dataScope.GetAccessibleOrgUnitIdsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((IReadOnlySet<Guid>?)null);
        _dataScope.GetVisibleEmployeeIdsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((IReadOnlySet<Guid>?)null);
        services.AddSingleton(_dataScope);
        services.AddSingleton(Substitute.For<IAccessSettingsService>());
        services.AddSingleton(Substitute.For<Sense.Modules.Identity.Contracts.IUserAccountLookup>());

        // Employee only consumes ILegislationParameterReader (owned/registered by the Leave module in production);
        // stubbed here instead of pulling the whole Leave module in as a test dependency.
        services.AddSingleton(_legislation);

        // Same treatment for Assets.Contracts.IAssetLookup (registered by AssetsModule in production, not pulled in
        // here); unconfigured NSubstitute calls return 0, matching "no open assets" unless a test configures it.
        services.AddSingleton(_assetLookup);

        services.AddSingleton<IIntegrationEventHandler<ContractEvents.EmployeeTerminated>>(_terminated);
    }

    protected override async Task OnDatabaseReadyAsync()
    {
        await ServiceProvider!.GetRequiredService<OrganizationDbContext>().Database.MigrateAsync();
        await ServiceProvider!.GetRequiredService<EmployeeDbContext>().Database.MigrateAsync();
    }

    [Fact]
    public async Task Terminate_FromActiveAndSuspended_AppliesReasonDefaultsAndRejectsInvalidTransitions()
    {
        var tenantId = Guid.NewGuid();
        SetUser(tenantId);
        var hireDate = new DateOnly(2022, 1, 10);
        var terminationDate = new DateOnly(2026, 6, 1);

        // Draft (no hire date) can never be terminated.
        var draft = await ExecuteCommandAsync(new CreateEmployeeCommand("Deniz", "Draft", null, null, null, null, null, null, null, null));
        var anyReasonId = (await ExecuteQueryAsync(new ListTerminationReasonsQuery())).Value.First().Id;
        (await ExecuteCommandAsync(new TerminateEmployeeCommand(draft.Value.Id, terminationDate, anyReasonId)))
            .Error.Code.ShouldBe(EmployeeDomainErrors.TerminateInvalidStatus);

        // Active -> Terminated: no override supplied, the chosen reason's defaults (Resignation / "29") apply.
        var active = await ExecuteCommandAsync(new CreateEmployeeCommand("Ela", "Active", null, null, null, null, hireDate, null, null, null));
        var resignation = (await ExecuteQueryAsync(new ListTerminationReasonsQuery())).Value.Single(r => r.Code == "Resignation");
        resignation.DefaultTerminationType.ShouldBe(TerminationType.Resignation);
        resignation.DefaultSgkExitCode.ShouldBe("29");

        var terminated = await ExecuteCommandAsync(new TerminateEmployeeCommand(active.Value.Id, terminationDate, resignation.Id));
        terminated.IsSuccess.ShouldBeTrue();
        terminated.Value.Employee.Status.ShouldBe(nameof(EmployeeStatus.Terminated));
        terminated.Value.Employee.TerminationDate.ShouldBe(terminationDate);
        terminated.Value.Employee.TerminationReasonId.ShouldBe(resignation.Id);
        terminated.Value.Employee.TerminationType.ShouldBe(TerminationType.Resignation);
        terminated.Value.Employee.SgkExitCode.ShouldBe("29");

        // Already Terminated cannot be terminated again.
        (await ExecuteCommandAsync(new TerminateEmployeeCommand(active.Value.Id, terminationDate, resignation.Id)))
            .Error.Code.ShouldBe(EmployeeDomainErrors.TerminateInvalidStatus);

        // Suspended -> Terminated: explicit overrides win over the reason's defaults.
        var suspendedTarget = await ExecuteCommandAsync(new CreateEmployeeCommand("Kaan", "Suspended", null, null, null, null, hireDate, null, null, null));
        (await ExecuteCommandAsync(new SuspendEmployeeCommand(suspendedTarget.Value.Id, SuspensionReason.Other))).IsSuccess.ShouldBeTrue();
        var terminatedFromSuspended = await ExecuteCommandAsync(new TerminateEmployeeCommand(
            suspendedTarget.Value.Id, terminationDate, resignation.Id, TerminationType.EmployerJustCause_Art25, "99"));
        terminatedFromSuspended.IsSuccess.ShouldBeTrue();
        terminatedFromSuspended.Value.Employee.Status.ShouldBe(nameof(EmployeeStatus.Terminated));
        terminatedFromSuspended.Value.Employee.TerminationType.ShouldBe(TerminationType.EmployerJustCause_Art25);
        terminatedFromSuspended.Value.Employee.SgkExitCode.ShouldBe("99");

        // Termination date before hire date is rejected.
        var lateHire = await ExecuteCommandAsync(new CreateEmployeeCommand("Su", "LateHire", null, null, null, null, hireDate, null, null, null));
        (await ExecuteCommandAsync(new TerminateEmployeeCommand(lateHire.Value.Id, hireDate.AddDays(-1), resignation.Id)))
            .Error.Code.ShouldBe(EmployeeDomainErrors.TerminationDateBeforeHireDate);

        using (var scope = ServiceProvider!.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<OutboxProcessor<EmployeeDbContext>>().ProcessAsync();
        }

        _terminated.Received.Count.ShouldBe(2);
        var first = _terminated.Received.Single(e => e.EmployeeId == active.Value.Id);
        first.TerminationType.ShouldBe(ContractEvents.TerminationType.Resignation);
        first.ReasonCode.ShouldBe("Resignation");
        first.SgkExitCode.ShouldBe("29");
        first.TerminationDate.ShouldBe(terminationDate, "S-20: published event carries the command's termination date");

        var second = _terminated.Received.Single(e => e.EmployeeId == suspendedTarget.Value.Id);
        second.TerminationType.ShouldBe(ContractEvents.TerminationType.EmployerJustCause_Art25);
        second.SgkExitCode.ShouldBe("99");
        second.TerminationDate.ShouldBe(terminationDate);
    }

    /// <summary>
    /// Job-security scope check (JobSecurityEvaluator): headcount/seniority thresholds are stubbed to 1 employee / 3
    /// months (instead of the real seed's 30/6) so each scenario only needs a couple of employees. Each scenario uses
    /// its own tenant so headcount from one scenario never leaks into another.
    /// </summary>
    [Fact]
    public async Task JobSecurityScope_InScopeAndArt18ShowsWarning_OtherwiseNoWarning()
    {
        StubLegislationThresholds(minHeadcount: 1m, minSeniorityMonths: 3m);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var seniorHire = today.AddMonths(-5);
        var juniorHire = today.AddMonths(-1);

        // In scope (headcount > 1, seniority > 3 months, Indefinite, not a representative) + Art18 -> warning.
        var inScope = await TerminateWithScopeAsync(seniorHire, ContractType.Indefinite, isEmployerRepresentative: false, withSecondActiveEmployee: true);
        inScope.Value.JobSecurityInScope.ShouldBeTrue();
        inScope.Value.JobSecurityWarning.ShouldBeTrue();
        inScope.Value.JobSecurityWarningMessage.ShouldNotBeNullOrWhiteSpace();

        // Out of scope by headcount: only the target itself is active (1, not > 1).
        var lowHeadcount = await TerminateWithScopeAsync(seniorHire, ContractType.Indefinite, isEmployerRepresentative: false, withSecondActiveEmployee: false);
        lowHeadcount.Value.JobSecurityInScope.ShouldBeFalse();
        lowHeadcount.Value.JobSecurityWarning.ShouldBeFalse();
        lowHeadcount.Value.JobSecurityWarningMessage.ShouldBeNull();

        // Out of scope by seniority (1 month < 3 month floor).
        var lowSeniority = await TerminateWithScopeAsync(juniorHire, ContractType.Indefinite, isEmployerRepresentative: false, withSecondActiveEmployee: true);
        lowSeniority.Value.JobSecurityInScope.ShouldBeFalse();
        lowSeniority.Value.JobSecurityWarning.ShouldBeFalse();

        // Fixed-term contract never shows the warning, even when headcount/seniority are otherwise in scope.
        var fixedTerm = await TerminateWithScopeAsync(seniorHire, ContractType.FixedTerm, isEmployerRepresentative: false, withSecondActiveEmployee: true);
        fixedTerm.Value.JobSecurityInScope.ShouldBeFalse();
        fixedTerm.Value.JobSecurityWarning.ShouldBeFalse();

        // Employer representative is always excluded from scope.
        var representative = await TerminateWithScopeAsync(seniorHire, ContractType.Indefinite, isEmployerRepresentative: true, withSecondActiveEmployee: true);
        representative.Value.JobSecurityInScope.ShouldBeFalse();
        representative.Value.JobSecurityWarning.ShouldBeFalse();
    }

    /// <summary>
    /// Offboarding &lt;-&gt; zimmet (Assets.Contracts.IAssetLookup): an employee with open (unreturned) assets can still
    /// be terminated - the count is advisory only (surfaced in the result DTO for the web exit dialog), it never
    /// blocks the wizard, mirroring the job-security/collective-dismissal warnings.
    /// </summary>
    [Fact]
    public async Task Terminate_WithOpenAssets_SucceedsAndReportsTheCount()
    {
        var tenantId = Guid.NewGuid();
        SetUser(tenantId);
        var hireDate = new DateOnly(2022, 1, 10);
        var terminationDate = new DateOnly(2026, 6, 1);

        var employee = await ExecuteCommandAsync(new CreateEmployeeCommand("Zimmetli", "Çalışan", null, null, null, null, hireDate, null, null, null));
        var reasonId = (await ExecuteQueryAsync(new ListTerminationReasonsQuery())).Value.Single(r => r.Code == "Resignation").Id;

        _assetLookup.CountOpenAssetsAsync(tenantId, employee.Value.Id, Arg.Any<CancellationToken>()).Returns(2);

        var terminated = await ExecuteCommandAsync(new TerminateEmployeeCommand(employee.Value.Id, terminationDate, reasonId));
        terminated.IsSuccess.ShouldBeTrue("open assets must never block the exit wizard");
        terminated.Value.Employee.Status.ShouldBe(nameof(EmployeeStatus.Terminated));
        terminated.Value.OpenAssetCount.ShouldBe(2);
    }

    /// <summary>Tenant-authored termination reasons ("kiracı kendi nedenini oluşturur"): create, rename, deactivate, and terminate with one.</summary>
    [Fact]
    public async Task TerminationReasons_TenantCanAddRenameAndDeactivateItsOwn()
    {
        var tenantId = Guid.NewGuid();
        SetUser(tenantId);

        var created = await ExecuteCommandAsync(new CreateTerminationReasonCommand(
            "ProjectEnded", "Proje sonu", "22", TerminationType.Other, 200));
        created.IsSuccess.ShouldBeTrue();
        created.Value.IsSystem.ShouldBeFalse();
        created.Value.IsActive.ShouldBeTrue();

        // Codes are tenant-unique, including against the auto-seeded system rows.
        (await ExecuteCommandAsync(new CreateTerminationReasonCommand("ProjectEnded", "Kopya")))
            .Error.Code.ShouldBe(EmployeeDomainErrors.TerminationReasonCodeTaken);
        (await ExecuteCommandAsync(new CreateTerminationReasonCommand("Resignation", "Kopya")))
            .Error.Code.ShouldBe(EmployeeDomainErrors.TerminationReasonCodeTaken);

        var renamed = await ExecuteCommandAsync(new UpdateTerminationReasonCommand(
            created.Value.Id, "Proje bitişi", "23", TerminationType.FixedTermExpiry, 210));
        renamed.Value.Name.ShouldBe("Proje bitişi");
        renamed.Value.Code.ShouldBe("ProjectEnded", "the code never changes");
        renamed.Value.DefaultSgkExitCode.ShouldBe("23");

        // The custom reason pre-fills the wizard just like a system one.
        var hireDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1);
        var employee = await ExecuteCommandAsync(new CreateEmployeeCommand("Özel", "Neden", null, null, null, null, hireDate, null, null, null));
        var terminated = await ExecuteCommandAsync(new TerminateEmployeeCommand(employee.Value.Id, hireDate.AddMonths(6), created.Value.Id));
        terminated.Value.Employee.TerminationType.ShouldBe(TerminationType.FixedTermExpiry);
        terminated.Value.Employee.SgkExitCode.ShouldBe("23");

        // Deactivating hides it from the wizard's dropdown but keeps it for the terminated employee.
        (await ExecuteCommandAsync(new DeactivateTerminationReasonCommand(created.Value.Id))).Value.IsActive.ShouldBeFalse();
        (await ExecuteQueryAsync(new ListTerminationReasonsQuery())).Value.ShouldNotContain(r => r.Id == created.Value.Id);
        (await ExecuteQueryAsync(new ListTerminationReasonsQuery(IncludeInactive: true))).Value.ShouldContain(r => r.Id == created.Value.Id);

        (await ExecuteCommandAsync(new ReactivateTerminationReasonCommand(created.Value.Id))).Value.IsActive.ShouldBeTrue();
        (await ExecuteQueryAsync(new ListTerminationReasonsQuery())).Value.ShouldContain(r => r.Id == created.Value.Id);
    }

    /// <summary>
    /// Collective dismissal ("Toplu işten çıkarma eşik uyarısı", 4857 md.29): the tier table is stubbed down to
    /// "2+ employees -> 2 dismissals" so a handful of employees is enough, and only md.18 dismissals count - a
    /// resignation in the same window does not move the counter.
    /// </summary>
    [Fact]
    public async Task CollectiveDismissal_CountsOnlyArt18DismissalsInTheWindow()
    {
        StubLegislationThresholds(minHeadcount: 1m, minSeniorityMonths: 3m);
        StubCollectiveDismissalTiers("""[{"minHeadcount":2,"maxHeadcount":null,"count":2,"percent":null}]""");

        var tenantId = Guid.NewGuid();
        SetUser(tenantId);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var hireDate = today.AddYears(-2);

        var reasons = (await ExecuteQueryAsync(new ListTerminationReasonsQuery())).Value;
        var employerTermination = reasons.Single(r => r.DefaultTerminationType == TerminationType.ValidCause_Art18);
        var resignation = reasons.Single(r => r.Code == "Resignation");

        var ids = new List<Guid>();
        for (var i = 0; i < 4; i++)
        {
            ids.Add((await ExecuteCommandAsync(new CreateEmployeeCommand($"Toplu{i}", "Çıkış", null, null, null, null, hireDate, null, null, null))).Value.Id);
        }

        // Nothing dismissed yet: in scope (headcount 4 >= 2) but below the threshold.
        var initial = (await ExecuteQueryAsync(new GetCollectiveDismissalStatusQuery())).Value;
        initial.InScope.ShouldBeTrue();
        initial.Threshold.ShouldBe(2);
        initial.DismissalsInWindow.ShouldBe(0);
        initial.ShowWarning.ShouldBeFalse();
        initial.WindowStart.ShouldBe(today.AddDays(-29));

        // A resignation is excluded from the count by the statute.
        (await ExecuteCommandAsync(new TerminateEmployeeCommand(ids[0], today, resignation.Id))).Value
            .CollectiveDismissal!.DismissalsInWindow.ShouldBe(0);

        // First md.18 dismissal: counted, still below the threshold of 2.
        var first = (await ExecuteCommandAsync(new TerminateEmployeeCommand(ids[1], today, employerTermination.Id))).Value;
        first.CollectiveDismissal!.DismissalsInWindow.ShouldBe(1);
        first.CollectiveDismissal.ShowWarning.ShouldBeFalse();

        // Second one reaches the threshold and surfaces the notification duty.
        var second = (await ExecuteCommandAsync(new TerminateEmployeeCommand(ids[2], today, employerTermination.Id))).Value;
        second.CollectiveDismissal!.DismissalsInWindow.ShouldBe(2);
        second.CollectiveDismissal.ShowWarning.ShouldBeTrue();
        second.CollectiveDismissal.NoticeDays.ShouldBe(30);
        second.CollectiveDismissal.WarningMessage.ShouldNotBeNullOrWhiteSpace();

        // A dismissal dated outside the window does not count towards it.
        var older = today.AddDays(-45);
        (await ExecuteCommandAsync(new TerminateEmployeeCommand(ids[3], older, employerTermination.Id))).Value
            .CollectiveDismissal!.DismissalsInWindow.ShouldBe(1, "only the pending one falls in its own window");

        var afterwards = (await ExecuteQueryAsync(new GetCollectiveDismissalStatusQuery())).Value;
        afterwards.DismissalsInWindow.ShouldBe(2, "the 45-day-old dismissal is outside today's window");
    }

    private void StubCollectiveDismissalTiers(string json) =>
        _legislation.FindAsync(LegislationParameterKeys.CollectiveDismissalThresholds, Arg.Any<DateOnly>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new LegislationParameterValue(
                LegislationParameterKeys.CollectiveDismissalThresholds, "TR", null, json, new DateOnly(2000, 1, 1), null, "test"));

    private void StubLegislationThresholds(decimal minHeadcount, decimal minSeniorityMonths)
    {
        _legislation.FindAsync(LegislationParameterKeys.JobSecurityMinHeadcount, Arg.Any<DateOnly>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new LegislationParameterValue(LegislationParameterKeys.JobSecurityMinHeadcount, "TR", minHeadcount, null, new DateOnly(2000, 1, 1), null, "test"));
        _legislation.FindAsync(LegislationParameterKeys.JobSecurityMinSeniorityMonths, Arg.Any<DateOnly>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new LegislationParameterValue(LegislationParameterKeys.JobSecurityMinSeniorityMonths, "TR", minSeniorityMonths, null, new DateOnly(2000, 1, 1), null, "test"));
    }

    /// <summary>Creates a fresh tenant, a termination target with the given profile (plus a second active employee when asked, to move headcount above the stubbed floor), and terminates it with TerminationType.ValidCause_Art18.</summary>
    private async Task<Result<TerminateEmployeeResultDto>> TerminateWithScopeAsync(
        DateOnly hireDate, ContractType contractType, bool isEmployerRepresentative, bool withSecondActiveEmployee)
    {
        var tenantId = Guid.NewGuid();
        SetUser(tenantId);

        var target = await ExecuteCommandAsync(new CreateEmployeeCommand(
            "Scope", "Target", null, null, null, null, hireDate, null, null, null, ContractType: contractType));

        if (isEmployerRepresentative)
        {
            var db = ServiceProvider!.GetRequiredService<EmployeeDbContext>();
            var entity = await db.Employees.FirstAsync(e => e.Id == target.Value.Id);
            entity.SetEmployerRepresentative(true);
            await db.SaveChangesAsync();
        }

        if (withSecondActiveEmployee)
        {
            await ExecuteCommandAsync(new CreateEmployeeCommand("Scope", "Peer", null, null, null, null, hireDate, null, null, null));
        }

        var reasonId = (await ExecuteQueryAsync(new ListTerminationReasonsQuery())).Value.Single(r => r.Code == "EmployerValidCause").Id;
        return await ExecuteCommandAsync(new TerminateEmployeeCommand(
            target.Value.Id, DateOnly.FromDateTime(DateTime.UtcNow), reasonId, TerminationType.ValidCause_Art18));
    }

    private void SetUser(Guid tenantId)
    {
        ((CurrentUserAccessor)CurrentUser!).Set(new AuthenticatedUser(
            UserId: Guid.NewGuid(), EmployeeId: null, Email: "hr@test.local", DisplayName: "HR",
            Roles: new HashSet<string>(StringComparer.Ordinal), IsPlatformAdmin: false,
            ImpersonatorUserId: null, ApiKeyId: null, CorrelationId: null, IpAddress: "127.0.0.1"));
        TenantContextSetter!.BeginScope(tenantId);
    }
}

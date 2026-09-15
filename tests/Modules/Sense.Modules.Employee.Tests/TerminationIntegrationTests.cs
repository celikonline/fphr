using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
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
public sealed class TerminationIntegrationTests : TestFixture
{
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();
    private readonly ILegislationParameterReader _legislation = Substitute.For<ILegislationParameterReader>();
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

        var second = _terminated.Received.Single(e => e.EmployeeId == suspendedTarget.Value.Id);
        second.TerminationType.ShouldBe(ContractEvents.TerminationType.EmployerJustCause_Art25);
        second.SgkExitCode.ShouldBe("99");
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

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Employee.Api;
using Sense.Modules.Employee.Application.Career;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Application.EmploymentPeriods;
using Sense.Modules.Employee.Application.Terminations;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Organization.Api;
using Sense.Modules.Organization.Infrastructure.Persistence;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Legislation;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Context;
using Sense.Shared.Infrastructure.Persistence.Outbox;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;
using ContractEvents = Sense.Modules.Employee.Contracts.Events;

namespace Sense.Modules.Employee.Tests;

/// <summary>
/// Employment periods end to end (Faz 1a E5a, 11.3.03): terminating closes the open period with its settlement flags,
/// GET /employees/{id}/employment-periods reports the merged seniority, and "Yeniden işe al" opens a new period,
/// carries the counted leave seniority over and publishes EmployeeRehired through the outbox.
/// </summary>
[Collection("PostgreSQL")]
public sealed class EmploymentPeriodIntegrationTests : TestFixture
{
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();
    private readonly RecordingHandler<ContractEvents.EmployeeRehired> _rehired = new();

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

        // Employee only consumes ILegislationParameterReader (owned by the Leave module in production).
        services.AddSingleton(Substitute.For<ILegislationParameterReader>());

        services.AddSingleton<IIntegrationEventHandler<ContractEvents.EmployeeRehired>>(_rehired);
    }

    protected override async Task OnDatabaseReadyAsync()
    {
        await ServiceProvider!.GetRequiredService<OrganizationDbContext>().Database.MigrateAsync();
        await ServiceProvider!.GetRequiredService<EmployeeDbContext>().Database.MigrateAsync();
    }

    [Fact]
    public async Task TerminateThenRehire_ClosesThePeriodAndCarriesTheCountedLeaveSeniority()
    {
        var tenantId = Guid.NewGuid();
        SetUser(tenantId);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var hireDate = today.AddYears(-3);
        var exitDate = today.AddYears(-2);
        var rehireDate = today.AddDays(-30);
        var firstPeriodDays = exitDate.DayNumber - hireDate.DayNumber + 1;

        var created = await ExecuteCommandAsync(new CreateEmployeeCommand("Umut", "Dönem", null, null, null, null, hireDate, null, null, null));
        var employeeId = created.Value.Id;

        // An active employee has no stored period yet: the read side shows the implicit one built from the hire date.
        var beforeExit = (await ExecuteQueryAsync(new GetEmploymentHistoryQuery(employeeId))).Value;
        beforeExit.Periods.Single().StartDate.ShouldBe(hireDate);
        beforeExit.Periods.Single().EndDate.ShouldBeNull();
        beforeExit.Seniority.LeaveSeniorityStartDate.ShouldBe(hireDate);

        // Rehire is only for terminated employees.
        (await ExecuteCommandAsync(new RehireEmployeeCommand(employeeId, rehireDate)))
            .Error.Code.ShouldBe(EmployeeDomainErrors.RehireInvalidStatus);

        var reason = (await ExecuteQueryAsync(new ListTerminationReasonsQuery())).Value.Single(r => r.Code == "Resignation");
        var terminated = await ExecuteCommandAsync(new TerminateEmployeeCommand(
            employeeId, exitDate, reason.Id, SeverancePaid: true, LeaveSettledAtExit: false));
        terminated.IsSuccess.ShouldBeTrue();

        var afterExit = (await ExecuteQueryAsync(new GetEmploymentHistoryQuery(employeeId))).Value;
        var closed = afterExit.Periods.Single();
        closed.StartDate.ShouldBe(hireDate);
        closed.EndDate.ShouldBe(exitDate);
        closed.Source.ShouldBe(EmploymentPeriodSource.Hire);
        closed.SeverancePaid.ShouldBeTrue();
        closed.LeaveSettledAtExit.ShouldBeFalse();
        closed.TerminationReasonName.ShouldBe(reason.Name);

        // Seniority of a terminated employee is measured at the exit date, not today.
        afterExit.Seniority.AsOf.ShouldBe(exitDate);
        afterExit.Seniority.LeaveSeniorityDays.ShouldBe(firstPeriodDays);
        afterExit.Seniority.SeveranceSeniorityDays.ShouldBe(0, "severance was paid out for this period");

        var rehired = await ExecuteCommandAsync(new RehireEmployeeCommand(employeeId, rehireDate));
        rehired.IsSuccess.ShouldBeTrue();
        rehired.Value.Employee.Status.ShouldBe(nameof(EmployeeStatus.Active));
        rehired.Value.Employee.HireDate.ShouldBe(rehireDate);
        rehired.Value.Employee.TerminationDate.ShouldBeNull();
        rehired.Value.Employee.LeaveAccrualStartDate.ShouldBe(rehireDate.AddDays(-firstPeriodDays));

        var periods = rehired.Value.EmploymentHistory.Periods;
        periods.Count.ShouldBe(2);
        periods[1].Source.ShouldBe(EmploymentPeriodSource.Rehire);
        periods[1].EndDate.ShouldBeNull();
        rehired.Value.EmploymentHistory.Seniority.SeveranceSeniorityStartDate.ShouldBe(rehireDate);

        // The new period opens a career record too, so the timeline shows why the employee came back.
        var career = (await ExecuteQueryAsync(new GetEmployeeCareerQuery(employeeId))).Value;
        career[0].ChangeType.ShouldBe(CareerChangeType.Rehire);
        career[0].ValidFrom.ShouldBe(rehireDate);

        using (var scope = ServiceProvider!.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<OutboxProcessor<EmployeeDbContext>>().ProcessAsync();
        }

        var published = _rehired.Received.Single(e => e.EmployeeId == employeeId);
        published.HireDate.ShouldBe(rehireDate);
        published.LeaveAccrualStartDate.ShouldBe(rehireDate.AddDays(-firstPeriodDays));
        published.SeniorityStartDate.ShouldBe(rehireDate);
        published.PreviousPeriodsMerged.ShouldBeTrue();
    }

    /// <summary>A period settled at exit ("izinleri kullandırıldı / ücreti ödendi") never comes back into leave seniority.</summary>
    [Fact]
    public async Task Rehire_AfterALeaveSettledExit_StartsLeaveSeniorityFromTheNewHireDate()
    {
        var tenantId = Guid.NewGuid();
        SetUser(tenantId);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var hireDate = today.AddYears(-3);
        var exitDate = today.AddYears(-2);
        var rehireDate = today.AddDays(-10);

        var created = await ExecuteCommandAsync(new CreateEmployeeCommand("Selin", "Tasfiye", null, null, null, null, hireDate, null, null, null));
        var reason = (await ExecuteQueryAsync(new ListTerminationReasonsQuery())).Value.Single(r => r.Code == "Resignation");

        (await ExecuteCommandAsync(new TerminateEmployeeCommand(
            created.Value.Id, exitDate, reason.Id, SeverancePaid: false, LeaveSettledAtExit: true))).IsSuccess.ShouldBeTrue();

        var rehired = await ExecuteCommandAsync(new RehireEmployeeCommand(created.Value.Id, rehireDate));

        rehired.Value.Employee.LeaveAccrualStartDate.ShouldBe(rehireDate);
        rehired.Value.EmploymentHistory.Seniority.LeaveSeniorityStartDate.ShouldBe(rehireDate);

        // Severance seniority still carries the first period: only its leave was settled.
        var firstPeriodDays = exitDate.DayNumber - hireDate.DayNumber + 1;
        rehired.Value.EmploymentHistory.Seniority.SeveranceSeniorityStartDate.ShouldBe(rehireDate.AddDays(-firstPeriodDays));
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

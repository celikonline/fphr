using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Attendance.Infrastructure;
using Sense.Modules.Attendance.Infrastructure.Persistence;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Lookups;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Leave.Application;
using Sense.Modules.Leave.Application.Balances;
using Sense.Modules.Leave.Application.Calendar;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Modules.Leave.Infrastructure;
using Sense.Modules.Leave.Infrastructure.Persistence;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Kernel.Results;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Modules.Leave.Tests;

/// <summary>
/// FG-007 "Ekibim" izinleri on real PostgreSQL, with Employee's real IEmployeeLookup over real manager links: GET leave/me/team/requests
/// lists only the caller's direct reports' pending/approved leave - never another manager's report, an indirect report or a
/// rejected request - and masks sensitive types like the calendar.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class MyTeamLeaveIntegrationTests : TestFixture
{
    private static readonly DateOnly From = new(2026, 10, 1);
    private static readonly DateOnly To = new(2026, 10, 31);
    private static readonly DateTime Now = new(2026, 9, 28, 8, 0, 0, DateTimeKind.Utc);

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IEmployeeDirectory _directory = Substitute.For<IEmployeeDirectory>();

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        services.AddModuleDbContext<LeaveDbContext>(configuration, LeaveDbContext.SchemaName);
        services.AddModuleDbContext<AttendanceDbContext>(configuration, AttendanceDbContext.SchemaName);
        services.AddAttendanceCoreServices(configuration);
        services.AddLeaveServices(configuration);
        services.AddModuleHandlers(
            LeaveDbContext.SchemaName, typeof(LeaveErrors).Assembly, typeof(LeavePermissions).Assembly, typeof(LeaveDbContext).Assembly,
            typeof(ILeaveRuleSetRepository).Assembly);
        services.AddModuleDbContext<EmployeeDbContext>(configuration, EmployeeDbContext.SchemaName);
        services.AddScoped<IEmployeeLookup, EmployeeLookup>();
    }

    protected override async Task OnDatabaseReadyAsync()
    {
        await ServiceProvider!.GetRequiredService<LeaveDbContext>().Database.MigrateAsync();
        await ServiceProvider.GetRequiredService<EmployeeDbContext>().Database.MigrateAsync();
    }

    private sealed record Seeded(
        Guid TenantId, Guid ManagerA, Guid ManagerB, Guid Ayse, Guid Deniz, Guid AyseAnnual, Guid AyseSick, Guid DenizAnnual, Guid ElifAnnual);

    [Fact]
    public async Task EachManager_SeesOnlyTheirDirectReportsActiveLeave_WithSensitiveTypesMasked()
    {
        var s = await SeedAsync();
        using var tenantScope = TenantContextSetter!.BeginScope(s.TenantId);

        _currentUser.EmployeeId.Returns(s.ManagerA);
        var mine = (await ListAsync(From, To, null)).Value;

        mine.Select(e => e.RequestId).ShouldBe([s.AyseAnnual, s.AyseSick], ignoreOrder: true);
        mine.ShouldAllBe(e => e.EmployeeId == s.Ayse);
        var annual = mine.Single(e => e.RequestId == s.AyseAnnual);
        annual.Status.ShouldBe(LeaveRequestStatus.Pending);
        annual.IsMasked.ShouldBeFalse();
        annual.LeaveTypeCode.ShouldBe(LeaveTypeCode.Annual);
        annual.EmployeeName.ShouldBe("Ayşe Demir");
        var sick = mine.Single(e => e.RequestId == s.AyseSick);
        sick.Status.ShouldBe(LeaveRequestStatus.Approved);
        sick.IsMasked.ShouldBeTrue();
        sick.LeaveTypeCode.ShouldBeNull();
        sick.LeaveTypeName.ShouldBeNull();

        _currentUser.EmployeeId.Returns(s.ManagerB);
        (await ListAsync(From, To, null)).Value.Select(e => e.RequestId).ShouldBe([s.DenizAnnual]);
    }

    [Fact]
    public async Task StatusFilter_NarrowsToPendingOrApproved()
    {
        var s = await SeedAsync();
        using var tenantScope = TenantContextSetter!.BeginScope(s.TenantId);
        _currentUser.EmployeeId.Returns(s.ManagerA);

        (await ListAsync(From, To, LeaveRequestStatus.Pending)).Value.Select(e => e.RequestId).ShouldBe([s.AyseAnnual]);
        (await ListAsync(From, To, LeaveRequestStatus.Approved)).Value.Select(e => e.RequestId).ShouldBe([s.AyseSick]);
        (await ListAsync(From, To, LeaveRequestStatus.Rejected)).Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task DirectReportsOnly_NoReportsOrNoLinkedEmployeeIsEmpty_AndAnInvalidRangeFails()
    {
        var s = await SeedAsync();
        using var tenantScope = TenantContextSetter!.BeginScope(s.TenantId);

        // One level only: Elif is Ayşe's direct report (so Ayşe sees her leave) but not A's, and Ayşe never sees her own leave here.
        _currentUser.EmployeeId.Returns(s.Ayse);
        (await ListAsync(From, To, null)).Value.Select(e => e.RequestId).ShouldBe([s.ElifAnnual]);

        _currentUser.EmployeeId.Returns(s.Deniz);
        (await ListAsync(From, To, null)).Value.ShouldBeEmpty();

        _currentUser.EmployeeId.Returns((Guid?)null);
        (await ListAsync(From, To, null)).Value.ShouldBeEmpty();

        _currentUser.EmployeeId.Returns(s.ManagerA);
        (await ListAsync(To, From, null)).Error.Code.ShouldBe(LeaveDomainErrors.RequestInvalidRange);
        (await ListAsync(From, From.AddDays(LeaveLimits.CalendarMaxRangeDays), null)).Error.Code.ShouldBe(LeaveErrors.CalendarRangeTooLarge);
    }

    /// <summary>
    /// A manages Ayşe; B manages Deniz; Ayşe manages Elif (indirect for A). Ayşe: pending annual, approved sick, rejected annual;
    /// Deniz: approved annual; Elif: pending annual.
    /// </summary>
    private async Task<Seeded> SeedAsync()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        using var scope = ServiceProvider!.CreateScope();

        var employees = scope.ServiceProvider.GetRequiredService<EmployeeDbContext>();
        var managerA = NewEmployee(tenantId, "Ahmet", "Yılmaz", null);
        var managerB = NewEmployee(tenantId, "Burak", "Kaya", null);
        var ayse = NewEmployee(tenantId, "Ayşe", "Demir", managerA.Id);
        var deniz = NewEmployee(tenantId, "Deniz", "Ak", managerB.Id);
        var elif = NewEmployee(tenantId, "Elif", "Can", ayse.Id);
        employees.Employees.AddRange(managerA, managerB, ayse, deniz, elif);
        await employees.SaveChangesAsync(TestContext.Current.CancellationToken);

        var ruleSet = await scope.ServiceProvider.GetRequiredService<LeaveBalanceService>().EnsureDefaultRuleSetAsync(tenantId, TestContext.Current.CancellationToken);
        var annualType = ruleSet.Types.Single(t => t.Code == LeaveTypeCode.Annual);
        var sickType = ruleSet.Types.Single(t => t.Code == LeaveTypeCode.Sick);
        sickType.IsSensitive.ShouldBeTrue();

        var ayseAnnual = Request(tenantId, ayse.Id, annualType, new DateOnly(2026, 10, 5));
        var ayseSick = Approved(Request(tenantId, ayse.Id, sickType, new DateOnly(2026, 10, 12)));
        var ayseRejected = Request(tenantId, ayse.Id, annualType, new DateOnly(2026, 10, 19));
        var approvalId = Guid.NewGuid();
        ayseRejected.AttachApproval(approvalId);
        ayseRejected.Reject(approvalId, Now).IsSuccess.ShouldBeTrue();
        var denizAnnual = Approved(Request(tenantId, deniz.Id, annualType, new DateOnly(2026, 10, 5)));
        var elifAnnual = Request(tenantId, elif.Id, annualType, new DateOnly(2026, 10, 7));

        var leave = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
        leave.Requests.AddRange(ayseAnnual, ayseSick, ayseRejected, denizAnnual, elifAnnual);
        await leave.SaveChangesAsync(TestContext.Current.CancellationToken);

        _directory.GetContactsAsync(tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<EmployeeContact>
            {
                new(ayse.Id, "Ayşe", "Demir", null, null, null, "Active"),
                new(deniz.Id, "Deniz", "Ak", null, null, null, "Active"),
                new(elif.Id, "Elif", "Can", null, null, null, "Active"),
            });

        return new Seeded(tenantId, managerA.Id, managerB.Id, ayse.Id, deniz.Id, ayseAnnual.Id, ayseSick.Id, denizAnnual.Id, elifAnnual.Id);
    }

    private static EmployeeEntity NewEmployee(Guid tenantId, string firstName, string lastName, Guid? managerId) =>
        EmployeeEntity.Create(tenantId, new EmployeeInfo(firstName, lastName, null, null, null, null, null, null), null, null, managerId).Value;

    private static LeaveRequest Request(Guid tenantId, Guid employeeId, LeaveType type, DateOnly day) =>
        LeaveRequest.Submit(tenantId, employeeId, type.Id, type.Code, day, day, 1m, null, LeaveRequestSource.SelfService, Now, null, null).Value;

    private static LeaveRequest Approved(LeaveRequest request)
    {
        var approvalId = Guid.NewGuid();
        request.AttachApproval(approvalId);
        request.Approve(approvalId, Now).IsSuccess.ShouldBeTrue();
        return request;
    }

    private async Task<Result<IReadOnlyList<LeaveCalendarEntryDto>>> ListAsync(DateOnly from, DateOnly to, LeaveRequestStatus? status)
    {
        using var scope = ServiceProvider!.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<ListMyTeamLeaveRequestsHandler>(scope.ServiceProvider, _currentUser, _directory);
        return await handler.Handle(new ListMyTeamLeaveRequestsQuery(from, to, status), TestContext.Current.CancellationToken);
    }
}

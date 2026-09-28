using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Application.MyTeam;
using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Infrastructure;
using Sense.Modules.Attendance.Infrastructure.Persistence;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

/// <summary>
/// FG-007 "Ekibim" on real PostgreSQL: both managers' reports have attendance rows in the same tenant and week, and each manager's
/// week, month and today lists carry only their own direct reports (the direct-report set itself is covered by Employee's
/// MyTeamIntegrationTests against the real lookup).
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class MyTeamIntegrationTests : TestFixture
{
    // 2026-09-23 is a Wednesday; its week starts on 2026-09-21.
    private static readonly DateOnly Today = new(2026, 9, 23);
    private static readonly DateOnly Monday = new(2026, 9, 21);
    private readonly FixedLocalClock _clock = new(new DateTime(2026, 9, 23, 7, 0, 0, DateTimeKind.Utc));

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IEmployeeLookup _lookup = Substitute.For<IEmployeeLookup>();
    private readonly IEmployeeDirectory _directory = Substitute.For<IEmployeeDirectory>();

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        services.AddModuleDbContext<AttendanceDbContext>(configuration, AttendanceDbContext.SchemaName);
        services.AddAttendanceServices(configuration);
        services.AddModuleHandlers(
            AttendanceDbContext.SchemaName,
            typeof(AttendanceErrors).Assembly,
            typeof(AttendancePermissions).Assembly,
            typeof(AttendanceDbContext).Assembly,
            typeof(IWorkScheduleRepository).Assembly);
    }

    protected override async Task OnDatabaseReadyAsync() =>
        await ServiceProvider!.GetRequiredService<AttendanceDbContext>().Database.MigrateAsync();

    private sealed record Teams(Guid TenantId, Guid ManagerA, Guid ManagerB, Guid Ayse, Guid Burak);

    [Fact]
    public async Task EachManager_SeesOnlyTheirOwnDirectReports_InWeekMonthAndToday()
    {
        var teams = await ArrangeAsync();
        using var tenantScope = TenantContextSetter!.BeginScope(teams.TenantId);

        _currentUser.EmployeeId.Returns(teams.ManagerA);
        (await WeekAsync()).Select(r => r.EmployeeId).ShouldBe([teams.Ayse]);
        (await MonthAsync()).Select(r => r.EmployeeId).ShouldBe([teams.Ayse]);
        var today = (await TodayAsync()).ShouldHaveSingleItem();
        today.EmployeeId.ShouldBe(teams.Ayse);
        today.Status.ShouldBe(MyTeamTodayStatus.CheckedIn);
        today.CheckIn.ShouldBe(new TimeOnly(8, 45));

        _currentUser.EmployeeId.Returns(teams.ManagerB);
        (await WeekAsync()).Select(r => r.EmployeeId).ShouldBe([teams.Burak]);
        (await MonthAsync()).Select(r => r.EmployeeId).ShouldBe([teams.Burak]);
        (await TodayAsync()).ShouldHaveSingleItem().Status.ShouldBe(MyTeamTodayStatus.CheckedOut);

        // Ayşe's own attendance exists, but she manages nobody.
        _currentUser.EmployeeId.Returns(teams.Ayse);
        (await WeekAsync()).ShouldBeEmpty();
        (await MonthAsync()).ShouldBeEmpty();
        (await TodayAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task WeekRow_CarriesTheReportsStoredDays()
    {
        var teams = await ArrangeAsync();
        using var tenantScope = TenantContextSetter!.BeginScope(teams.TenantId);
        _currentUser.EmployeeId.Returns(teams.ManagerA);

        var row = (await WeekAsync()).ShouldHaveSingleItem();

        row.EmployeeName.ShouldBe("Ayşe Demir");
        row.Days.Count.ShouldBe(7);
        row.Days.Single(d => d.Date == Monday).WorkedMinutes.ShouldBeGreaterThan(0);
        row.Days.Single(d => d.Date == Today).AttendanceDayId.ShouldNotBeNull();
    }

    private async Task<Teams> ArrangeAsync()
    {
        var teams = new Teams(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _lookup.GetDirectReportIdsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new List<Guid>());
        _lookup.GetDirectReportIdsAsync(teams.TenantId, teams.ManagerA, Arg.Any<CancellationToken>()).Returns(new List<Guid> { teams.Ayse });
        _lookup.GetDirectReportIdsAsync(teams.TenantId, teams.ManagerB, Arg.Any<CancellationToken>()).Returns(new List<Guid> { teams.Burak });
        _directory.GetContactsAsync(teams.TenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => ((IReadOnlyCollection<Guid>)call[1])
                .Select(id => new EmployeeContact(id, id == teams.Ayse ? "Ayşe" : "Burak", id == teams.Ayse ? "Demir" : "Kaya", null, null, null, "Active"))
                .ToList());

        using var tenantScope = TenantContextSetter!.BeginScope(teams.TenantId);
        using var scope = ServiceProvider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        db.AttendanceDays.AddRange(
            Day(teams.TenantId, teams.Ayse, Monday, new TimeOnly(9, 0), new TimeOnly(18, 0)),
            Day(teams.TenantId, teams.Ayse, Today, new TimeOnly(8, 45), null),
            Day(teams.TenantId, teams.Burak, Monday, new TimeOnly(9, 0), new TimeOnly(18, 0)),
            Day(teams.TenantId, teams.Burak, Today, new TimeOnly(7, 0), new TimeOnly(7, 30)));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return teams;
    }

    private static AttendanceDay Day(Guid tenantId, Guid employeeId, DateOnly date, TimeOnly checkIn, TimeOnly? checkOut) =>
        AttendanceDay.Record(tenantId, employeeId, date, checkIn, checkOut, new TimeOnly(9, 0), new TimeOnly(18, 0), 540, 60, AttendanceDaySource.SelfService);

    private async Task<IReadOnlyList<TeamWeekRowDto>> WeekAsync()
    {
        using var scope = ServiceProvider!.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<GetMyTeamWeekHandler>(scope.ServiceProvider, _currentUser, _lookup, _directory);
        var result = await handler.Handle(new GetMyTeamWeekQuery(Today), TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
        return result.Value;
    }

    private async Task<IReadOnlyList<TeamWeekRowDto>> MonthAsync()
    {
        using var scope = ServiceProvider!.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<GetMyTeamMonthHandler>(scope.ServiceProvider, _currentUser, _lookup, _directory);
        var result = await handler.Handle(new GetMyTeamMonthQuery(Today), TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
        return result.Value;
    }

    private async Task<IReadOnlyList<MyTeamTodayDto>> TodayAsync()
    {
        using var scope = ServiceProvider!.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<GetMyTeamTodayHandler>(
            scope.ServiceProvider, _currentUser, _lookup, _directory, (ITenantLocalClock)_clock);
        var result = await handler.Handle(new GetMyTeamTodayQuery(), TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
        return result.Value;
    }
}

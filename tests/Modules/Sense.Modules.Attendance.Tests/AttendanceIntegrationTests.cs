using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.WorkSchedules;
using Sense.Modules.Attendance.Infrastructure;
using Sense.Modules.Attendance.Infrastructure.Persistence;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

/// <summary>
/// Real PostgreSQL (Testcontainers): migration, and Employee.Contracts.EmployeeHired delivered through IEventBus
/// the way Sense.Worker's OutboxProcessor delivers it (handler resolved from DI, tenant scope set by the bus).
/// </summary>
[Collection("PostgreSQL")]
public sealed class AttendanceIntegrationTests : TestFixture
{
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

    [Fact]
    public void WorkerRegistration_ResolvesEveryAttendanceIntegrationEventHandler()
    {
        var configuration = ServiceProvider!.GetRequiredService<IConfiguration>();
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddLogging();
        services.AddSenseCore(configuration);
        services.AddModuleDbContext<AttendanceDbContext>(configuration, AttendanceDbContext.SchemaName);
        services.AddAttendanceCoreServices(configuration);
        services.AddAttendanceIntegrationEventHandlers();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetServices<IIntegrationEventHandler<EmployeeHired>>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task EmployeeHired_AssignsDefaultScheduleFromHireDate_AndIsIdempotent()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var hireDate = new DateOnly(2026, 3, 1);
        var hired = new EmployeeHired(tenantId, employeeId, hireDate, hireDate);
        var bus = ServiceProvider!.GetRequiredService<IEventBus>();

        await bus.Publish(hired);
        await bus.Publish(hired);

        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();

        var assignment = await db.WorkScheduleAssignments.SingleAsync();
        assignment.EmployeeId.ShouldBe(employeeId);
        assignment.EffectiveFrom.ShouldBe(hireDate);
        assignment.Source.ShouldBe(WorkScheduleAssignmentSource.Hire);

        var schedule = await db.WorkSchedules.SingleAsync();
        schedule.IsDefault.ShouldBeTrue();
        assignment.WorkScheduleId.ShouldBe(schedule.Id);
    }

    [Fact]
    public async Task WorkSchedule_PersistedAndReloaded_RoundTripsDays()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        Guid scheduleId;

        var days = Enum.GetValues<DayOfWeek>()
            .Select(d => d is DayOfWeek.Saturday or DayOfWeek.Sunday
                ? WorkScheduleDay.Off(d)
                : WorkScheduleDay.Working(d, new TimeOnly(9, 0), new TimeOnly(18, 0), new TimeOnly(12, 30), new TimeOnly(13, 30)))
            .ToList();

        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<AttendanceDbContext>();
            var created = WorkSchedule.Create(tenantId, "Satış", days, includeSaturdayWhenFullWeek: true);
            created.IsSuccess.ShouldBeTrue();
            db.WorkSchedules.Add(created.Value);
            await db.SaveChangesAsync();
            scheduleId = created.Value.Id;
        }

        using var read = ServiceProvider.CreateScope();
        var repo = read.ServiceProvider.GetRequiredService<IWorkScheduleRepository>();
        var loaded = (await repo.GetByIdAsync(scheduleId)).Value;
        loaded.Days.Count.ShouldBe(7);
        loaded.IncludeSaturdayWhenFullWeek.ShouldBeTrue();
        loaded.GetDay(DayOfWeek.Monday).IsWorkingDay.ShouldBeTrue();
        loaded.GetDay(DayOfWeek.Monday).Start.ShouldBe(new TimeOnly(9, 0));
        loaded.GetDay(DayOfWeek.Saturday).IsWorkingDay.ShouldBeFalse();
        loaded.WeeklyMinutes.ShouldBe(5 * ((18 - 9) * 60 - 60));
    }

    [Fact]
    public async Task ReadService_FallsBackToDefaultSchedule_UntilEmployeeIsAssigned()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);

        using (var write = ServiceProvider!.CreateScope())
        {
            var assignmentService = write.ServiceProvider.GetRequiredService<Application.WorkSchedules.WorkScheduleAssignmentService>();
            var db = write.ServiceProvider.GetRequiredService<AttendanceDbContext>();
            await assignmentService.EnsureDefaultScheduleAsync(tenantId, TestContext.Current.CancellationToken);
            await db.SaveChangesAsync();
        }

        using var read = ServiceProvider.CreateScope();
        var readService = read.ServiceProvider.GetRequiredService<IWorkScheduleReadService>();
        var monday = new DateOnly(2026, 9, 21);
        var day = await readService.GetForDateAsync(employeeId, monday, TestContext.Current.CancellationToken);
        day.IsWorkingDay.ShouldBeTrue();
        day.Start.ShouldBe(new TimeOnly(9, 0));

        var saturday = monday.AddDays(5);
        (await readService.IsWorkingDayAsync(employeeId, saturday, TestContext.Current.CancellationToken)).ShouldBeFalse();
    }
}

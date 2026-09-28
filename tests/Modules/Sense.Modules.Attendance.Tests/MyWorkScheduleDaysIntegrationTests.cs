using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Application.WorkSchedules;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.WorkSchedules;
using Sense.Modules.Attendance.Infrastructure;
using Sense.Modules.Attendance.Infrastructure.Persistence;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Kernel.Results;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

/// <summary>
/// "Takvimim" self uçu (FG-020) gerçek PostgreSQL üzerinde: <see cref="IWorkScheduleReadService"/>'in ürettiği
/// varsayılan program günden güne doğru okunuyor ve 42 günlük aralık sınırı uygulanıyor.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class MyWorkScheduleDaysIntegrationTests : TestFixture
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();

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
    public async Task DefaultSchedule_ReturnsOneRowPerDay()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        await EnsureDefaultScheduleAsync(tenantId);
        var monday = new DateOnly(2026, 9, 21);

        var result = await QueryAsync(employeeId, new GetMyWorkScheduleDaysQuery(monday, monday.AddDays(6)));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(7);
        var mondayRow = result.Value[0];
        mondayRow.Date.ShouldBe(monday);
        mondayRow.IsWorkingDay.ShouldBeTrue();
        mondayRow.Start.ShouldBe(new TimeOnly(9, 0));
        mondayRow.End.ShouldBe(new TimeOnly(18, 0));

        var saturdayRow = result.Value[5];
        saturdayRow.IsWorkingDay.ShouldBeFalse();
        saturdayRow.Start.ShouldBeNull();
    }

    [Fact]
    public async Task RangeOfExactly42Days_Succeeds()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        await EnsureDefaultScheduleAsync(tenantId);
        var from = new DateOnly(2026, 9, 21);

        var result = await QueryAsync(employeeId, new GetMyWorkScheduleDaysQuery(from, from.AddDays(41)));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(42);
    }

    [Fact]
    public async Task RangeOver42Days_ReturnsRangeTooLong()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        await EnsureDefaultScheduleAsync(tenantId);
        var from = new DateOnly(2026, 9, 21);

        var result = await QueryAsync(employeeId, new GetMyWorkScheduleDaysQuery(from, from.AddDays(42)));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceErrors.WorkScheduleRangeTooLong);
    }

    private async Task EnsureDefaultScheduleAsync(Guid tenantId)
    {
        using var scope = ServiceProvider!.CreateScope();
        var assignmentService = scope.ServiceProvider.GetRequiredService<WorkScheduleAssignmentService>();
        var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        await assignmentService.EnsureDefaultScheduleAsync(tenantId, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<Result<IReadOnlyList<MyWorkScheduleDayDto>>> QueryAsync(Guid employeeId, GetMyWorkScheduleDaysQuery query)
    {
        _currentUser.EmployeeId.Returns(employeeId);
        using var scope = ServiceProvider!.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<GetMyWorkScheduleDaysHandler>(scope.ServiceProvider, _currentUser);
        return await handler.Handle(query, TestContext.Current.CancellationToken);
    }
}

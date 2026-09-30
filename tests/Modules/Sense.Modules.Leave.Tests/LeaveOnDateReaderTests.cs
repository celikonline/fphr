using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Attendance.Infrastructure;
using Sense.Modules.Attendance.Infrastructure.Persistence;
using Sense.Modules.Leave.Application;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Modules.Leave.Infrastructure;
using Sense.Modules.Leave.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Leave.Tests;

/// <summary>ILeaveOnDateReader (Attendance's İzinde list): approved leave covering one date, optionally narrowed to employees.</summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class LeaveOnDateReaderTests : TestFixture
{
    private static readonly DateOnly Day = new(2026, 6, 11);

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        services.AddModuleDbContext<LeaveDbContext>(configuration, LeaveDbContext.SchemaName);
        services.AddModuleDbContext<AttendanceDbContext>(configuration, AttendanceDbContext.SchemaName);
        services.AddAttendanceCoreServices(configuration);
        services.AddAttendanceIntegrationEventHandlers();
        services.AddLeaveServices(configuration);
        services.AddSingleton(DocumentLookupDoubles.Permissive());
        services.AddModuleHandlers(
            LeaveDbContext.SchemaName, typeof(LeaveErrors).Assembly, typeof(LeavePermissions).Assembly, typeof(LeaveDbContext).Assembly,
            typeof(ILeaveRuleSetRepository).Assembly, typeof(Attendance.Contracts.IWorkScheduleReadService).Assembly);
    }

    protected override async Task OnDatabaseReadyAsync()
    {
        await ServiceProvider!.GetRequiredService<LeaveDbContext>().Database.MigrateAsync();
        await ServiceProvider.GetRequiredService<AttendanceDbContext>().Database.MigrateAsync();
    }

    private async Task SeedAsync(Guid tenantId, Guid employeeId, DateOnly start, DateOnly end, bool approve)
    {
        using var scope = ServiceProvider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
        var request = LeaveRequest.Submit(
            tenantId, employeeId, Guid.NewGuid(), LeaveTypeCode.Annual, start, end, 1m, null, LeaveRequestSource.SelfService, DateTime.UtcNow, null, null).Value;
        if (approve)
        {
            var approvalId = Guid.NewGuid();
            request.AttachApproval(approvalId);
            request.Approve(approvalId, DateTime.UtcNow);
        }

        db.Requests.Add(request);
        await db.SaveChangesAsync();
    }

    private async Task<IReadOnlyList<EmployeeLeaveOnDate>> ListAsync(Guid tenantId, IReadOnlyCollection<Guid>? ids)
    {
        using var scope = ServiceProvider!.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ILeaveOnDateReader>().ListAsync(tenantId, Day, ids, CancellationToken.None);
    }

    [Fact]
    public async Task Lists_only_approved_leave_covering_the_date_for_the_tenant()
    {
        var tenant = Guid.NewGuid();
        var covered = Guid.NewGuid();
        var pending = Guid.NewGuid();
        var otherDay = Guid.NewGuid();
        await SeedAsync(tenant, covered, Day.AddDays(-1), Day.AddDays(1), approve: true);
        await SeedAsync(tenant, pending, Day, Day, approve: false);
        await SeedAsync(tenant, otherDay, Day.AddDays(2), Day.AddDays(3), approve: true);
        await SeedAsync(Guid.NewGuid(), Guid.NewGuid(), Day, Day, approve: true);

        var all = await ListAsync(tenant, null);

        all.Select(l => l.EmployeeId).ShouldBe([covered]);
        all[0].LeaveTypeCode.ShouldBe("Annual");
    }

    [Fact]
    public async Task Narrows_to_the_given_employees()
    {
        var tenant = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await SeedAsync(tenant, a, Day, Day, approve: true);
        await SeedAsync(tenant, b, Day, Day, approve: true);

        (await ListAsync(tenant, [a])).Select(l => l.EmployeeId).ShouldBe([a]);
        (await ListAsync(tenant, [])).ShouldBeEmpty();
    }
}

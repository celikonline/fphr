using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Overtime;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Leave.Infrastructure.Persistence;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Infrastructure.Persistence.Audit;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Leave.Tests.AuditLog;

/// <summary>
/// senseik-049: izin talebi oluşturma/onaylama ve fazla mesai talebi oluşturma/onaylama hiç denetim kaydına
/// düşmüyordu (senseik-018 yalnız Identity'yi kapsıyordu). Bu test, paylaşımlı AuditLogInterceptor'ın
/// LeaveDbContext'e bağlandığını ve LeaveRequest/OvertimeRequest'in Status alanındaki "Pending → Approved" gibi
/// geçişlerin alan bazlı diff olarak Details'e yazıldığını gerçek PostgreSQL'e karşı doğrular.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class LeaveAuditLogInterceptorIntegrationTests : TestFixture
{
    private static readonly Guid LeaveTypeId = Guid.NewGuid();

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        services.AddScoped<AuditLogInterceptor>();
        services.AddModuleDbContext<LeaveDbContext>(
            configuration, LeaveDbContext.SchemaName,
            (sp, options) => options.AddInterceptors(sp.GetRequiredService<AuditLogInterceptor>()));
    }

    protected override async Task OnDatabaseReadyAsync() =>
        await ServiceProvider!.GetRequiredService<LeaveDbContext>().Database.MigrateAsync();

    [Fact]
    public async Task SubmittingALeaveRequest_WritesACreateAuditLogEntry()
    {
        var db = ServiceProvider!.GetRequiredService<LeaveDbContext>();
        var tenantId = Guid.NewGuid();
        using var scope = TenantContextSetter!.BeginScope(tenantId);
        SetCurrentUser(tenantId, Guid.NewGuid(), "employee@test.local", new HashSet<string>(StringComparer.Ordinal));

        var request = LeaveRequest.Submit(
            tenantId, Guid.NewGuid(), LeaveTypeId, LeaveTypeCode.Annual,
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 3), 3m, "Yaz tatili",
            LeaveRequestSource.SelfService, DateTime.UtcNow, null, null).Value;
        db.Requests.Add(request);
        await db.SaveChangesAsync();

        var entry = await db.Set<AuditLogEntry>().AsNoTracking().SingleAsync(e => e.ResourceId == request.Id.ToString());
        entry.TenantId.ShouldBe(tenantId);
        entry.Action.ShouldBe(AuditActions.Create);
        entry.Resource.ShouldBe(nameof(LeaveRequest));
    }

    /// <summary>KVKK m.12: onay, genel "LeaveRequest update" yerine "Status: Pending → Approved" gösterir.</summary>
    [Fact]
    public async Task ApprovingALeaveRequest_WritesTheStatusTransitionInDetails()
    {
        var db = ServiceProvider!.GetRequiredService<LeaveDbContext>();
        var tenantId = Guid.NewGuid();
        using var scope = TenantContextSetter!.BeginScope(tenantId);
        SetCurrentUser(tenantId, Guid.NewGuid(), "manager@test.local", new HashSet<string>(StringComparer.Ordinal));

        var request = LeaveRequest.Submit(
            tenantId, Guid.NewGuid(), LeaveTypeId, LeaveTypeCode.Annual,
            new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 2), 2m, null,
            LeaveRequestSource.SelfService, DateTime.UtcNow, null, null).Value;
        db.Requests.Add(request);
        await db.SaveChangesAsync();

        request.ApproveWithoutWorkflow(DateTime.UtcNow).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();

        var entry = await db.Set<AuditLogEntry>().AsNoTracking()
            .Where(e => e.ResourceId == request.Id.ToString() && e.Action == AuditActions.Update)
            .OrderByDescending(e => e.OccurredAt)
            .FirstAsync();

        entry.Details.ShouldContain($"Status: {LeaveRequestStatus.Pending} → {LeaveRequestStatus.Approved}");
    }

    [Fact]
    public async Task SubmittingAnOvertimeRequest_WritesACreateAuditLogEntry()
    {
        var db = ServiceProvider!.GetRequiredService<LeaveDbContext>();
        var tenantId = Guid.NewGuid();
        using var scope = TenantContextSetter!.BeginScope(tenantId);
        SetCurrentUser(tenantId, Guid.NewGuid(), "employee@test.local", new HashSet<string>(StringComparer.Ordinal));

        var overtime = OvertimeRequest.Submit(
            tenantId, Guid.NewGuid(), new DateOnly(2026, 6, 10), new TimeOnly(18, 0), 90,
            OvertimeDayKind.Workday, 0, "Ay sonu kapanış", OvertimeRequestSource.Manual, DateTime.UtcNow, null, null).Value;
        db.OvertimeRequests.Add(overtime);
        await db.SaveChangesAsync();

        var entry = await db.Set<AuditLogEntry>().AsNoTracking().SingleAsync(e => e.ResourceId == overtime.Id.ToString());
        entry.Action.ShouldBe(AuditActions.Create);
        entry.Resource.ShouldBe(nameof(OvertimeRequest));
    }

    /// <summary>KVKK m.12: fazla mesai onayı da "Pending → Approved" alan bazlı diff'ini üretir.</summary>
    [Fact]
    public async Task ApprovingAnOvertimeRequest_WritesTheStatusTransitionInDetails()
    {
        var db = ServiceProvider!.GetRequiredService<LeaveDbContext>();
        var tenantId = Guid.NewGuid();
        using var scope = TenantContextSetter!.BeginScope(tenantId);
        SetCurrentUser(tenantId, Guid.NewGuid(), "manager@test.local", new HashSet<string>(StringComparer.Ordinal));

        var overtime = OvertimeRequest.Submit(
            tenantId, Guid.NewGuid(), new DateOnly(2026, 6, 11), new TimeOnly(19, 0), 60,
            OvertimeDayKind.Workday, 0, null, OvertimeRequestSource.Manual, DateTime.UtcNow, null, null).Value;
        db.OvertimeRequests.Add(overtime);
        await db.SaveChangesAsync();

        var approvalId = Guid.NewGuid();
        overtime.AttachApproval(approvalId);
        overtime.Approve(approvalId, DateTime.UtcNow).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();

        var entry = await db.Set<AuditLogEntry>().AsNoTracking()
            .Where(e => e.ResourceId == overtime.Id.ToString() && e.Action == AuditActions.Update)
            .OrderByDescending(e => e.OccurredAt)
            .FirstAsync();

        entry.Details.ShouldContain($"Status: {OvertimeRequestStatus.Pending} → {OvertimeRequestStatus.Approved}");
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Workflow.Domain.ApprovalRequests;
using Sense.Modules.Workflow.Infrastructure.Persistence;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Infrastructure.Persistence.Audit;
using Sense.Shared.Kernel.Results;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Workflow.Tests.AuditLog;

/// <summary>
/// senseik-049: onay süreçleri (ApprovalRequest - Leave/Overtime/Expense vb. talepler bunun üzerinden akar) hiç
/// denetim kaydına düşmüyordu. Bu test, paylaşımlı AuditLogInterceptor'ın WorkflowDbContext'e bağlandığını ve
/// ApprovalRequest.Status geçişinin ("Pending → Cancelled") alan bazlı diff olarak Details'e yazıldığını gerçek
/// PostgreSQL'e karşı doğrular - denetim kaydı ekranında "Approval" kaynak değeri artık böyle görünür.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class WorkflowAuditLogInterceptorIntegrationTests : TestFixture
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        services.AddScoped<AuditLogInterceptor>();
        services.AddModuleDbContext<WorkflowDbContext>(
            configuration, WorkflowDbContext.SchemaName,
            (sp, options) => options.AddInterceptors(sp.GetRequiredService<AuditLogInterceptor>()));
    }

    protected override async Task OnDatabaseReadyAsync() =>
        await ServiceProvider!.GetRequiredService<WorkflowDbContext>().Database.MigrateAsync();

    private static Result<ApprovalRequest> SubmitOneStepRequest(Guid tenantId, Guid requesterEmployeeId, Guid approverEmployeeId) =>
        ApprovalRequest.Submit(
            tenantId, "Leave", Guid.NewGuid(), requesterEmployeeId,
            [new ApprovalTaskSeed(1, approverEmployeeId, null, true)],
            policyId: null, policyNameAtSubmission: null, resolvedFromOrgUnitId: null, resolvedByInheritance: false,
            showCurrentApproverToRequester: true, DateTime.UtcNow, null, null);

    [Fact]
    public async Task SubmittingAnApprovalRequest_WritesACreateAuditLogEntry()
    {
        var db = ServiceProvider!.GetRequiredService<WorkflowDbContext>();
        var tenantId = Guid.NewGuid();
        using var scope = TenantContextSetter!.BeginScope(tenantId);
        SetCurrentUser(tenantId, Guid.NewGuid(), "employee@test.local", new HashSet<string>(StringComparer.Ordinal));

        var request = SubmitOneStepRequest(tenantId, Guid.NewGuid(), Guid.NewGuid()).Value;
        db.ApprovalRequests.Add(request);
        await db.SaveChangesAsync();

        var entry = await db.Set<AuditLogEntry>().AsNoTracking().SingleAsync(e => e.ResourceId == request.Id.ToString());
        entry.TenantId.ShouldBe(tenantId);
        entry.Action.ShouldBe(AuditActions.Create);
        entry.Resource.ShouldBe(nameof(ApprovalRequest));
    }

    /// <summary>KVKK m.12: iptal/karar, genel "ApprovalRequest update" yerine "Status: Pending → Cancelled" gösterir.</summary>
    [Fact]
    public async Task CancellingAnApprovalRequest_WritesTheStatusTransitionInDetails()
    {
        var db = ServiceProvider!.GetRequiredService<WorkflowDbContext>();
        var tenantId = Guid.NewGuid();
        var requesterEmployeeId = Guid.NewGuid();
        using var scope = TenantContextSetter!.BeginScope(tenantId);
        SetCurrentUser(tenantId, Guid.NewGuid(), "employee@test.local", new HashSet<string>(StringComparer.Ordinal));

        var request = SubmitOneStepRequest(tenantId, requesterEmployeeId, Guid.NewGuid()).Value;
        db.ApprovalRequests.Add(request);
        await db.SaveChangesAsync();

        request.Cancel(requesterEmployeeId, DateTime.UtcNow).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();

        var entry = await db.Set<AuditLogEntry>().AsNoTracking()
            .Where(e => e.ResourceId == request.Id.ToString() && e.Action == AuditActions.Update)
            .OrderByDescending(e => e.OccurredAt)
            .FirstAsync();

        entry.Details.ShouldContain($"Status: {ApprovalRequestStatus.Pending} → {ApprovalRequestStatus.Cancelled}");
    }
}

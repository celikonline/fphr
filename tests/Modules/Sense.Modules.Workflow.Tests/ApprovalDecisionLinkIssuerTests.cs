using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Workflow.Application;
using Sense.Modules.Workflow.Contracts;
using Sense.Modules.Workflow.Domain;
using Sense.Modules.Workflow.Domain.ApprovalRequests;
using Sense.Modules.Workflow.Infrastructure.Messaging;
using Sense.Modules.Workflow.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Workflow.Tests;

/// <summary>
/// IApprovalDecisionLinkIssuer (real PostgreSQL): a single-use decision link is minted (and only its hash stored) only
/// while the task is the pending request's current step with a concrete approver.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class ApprovalDecisionLinkIssuerTests : TestFixture
{
    private const string FrontendBaseUrl = "https://app.senseik.test";

    protected override void ConfigureServices(IServiceCollection services)
    {
        var baseConfiguration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        var configuration = new ConfigurationBuilder()
            .AddConfiguration(baseConfiguration)
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Frontend:BaseUrl"] = FrontendBaseUrl + "/" })
            .Build();

        services.AddModuleDbContext<WorkflowDbContext>(configuration, WorkflowDbContext.SchemaName);
        services.AddScoped<IApprovalRequestRepository, ApprovalRequestRepository>();
        services.AddWorkflowDecisionLinks(configuration);
    }

    protected override async Task OnDatabaseReadyAsync() =>
        await ServiceProvider!.GetRequiredService<WorkflowDbContext>().Database.MigrateAsync();

    [Fact]
    public async Task CurrentPendingStep_WithApprover_IssuesLink_AndStoresOnlyTheHash()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var request = await SubmitAsync(tenantId, [new ApprovalTaskSeed(1, Guid.NewGuid(), null, true)]);
        var taskId = request.Tasks.Single().Id;

        ApprovalDecisionLink? link;
        using (var scope = ServiceProvider!.CreateScope())
        {
            link = await scope.ServiceProvider.GetRequiredService<IApprovalDecisionLinkIssuer>().IssueAsync(tenantId, taskId);
        }

        link.ShouldNotBeNull();
        link.Url.ShouldStartWith(FrontendBaseUrl + "/approve/");
        link.ExpiresAtUtc.ShouldBeGreaterThan(DateTime.UtcNow);

        using var verifyScope = ServiceProvider!.CreateScope();
        var rawToken = Uri.UnescapeDataString(link.Url[(FrontendBaseUrl + "/approve/").Length..]);
        var tokens = await verifyScope.ServiceProvider.GetRequiredService<WorkflowDbContext>().EmailDecisionTokens.AsNoTracking()
            .Where(t => t.ApprovalTaskId == taskId).ToListAsync();
        var token = tokens.ShouldHaveSingleItem();
        token.TokenHash.ShouldNotBe(rawToken);
        token.TokenHash.ShouldBe(verifyScope.ServiceProvider.GetRequiredService<IEmailTokenGenerator>().Hash(rawToken));
        token.UsedAt.ShouldBeNull();
    }

    [Fact]
    public async Task NotCurrentStep_RoleQueueTask_DecidedRequest_OrOtherTenant_ReturnNull()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var approver1 = Guid.NewGuid();
        var twoSteps = await SubmitAsync(tenantId, [new ApprovalTaskSeed(1, approver1, null, true), new ApprovalTaskSeed(2, Guid.NewGuid(), null, true)]);
        var roleQueue = await SubmitAsync(tenantId, [new ApprovalTaskSeed(1, null, "HR", true)]);

        using (var scope = ServiceProvider!.CreateScope())
        {
            var issuer = scope.ServiceProvider.GetRequiredService<IApprovalDecisionLinkIssuer>();
            (await issuer.IssueAsync(tenantId, twoSteps.Tasks.Single(t => t.StepNumber == 2).Id)).ShouldBeNull();
            (await issuer.IssueAsync(tenantId, roleQueue.Tasks.Single().Id)).ShouldBeNull();
            (await issuer.IssueAsync(Guid.NewGuid(), twoSteps.Tasks.Single(t => t.StepNumber == 1).Id)).ShouldBeNull();
            (await issuer.IssueAsync(tenantId, Guid.NewGuid())).ShouldBeNull();
        }

        using (var decideScope = ServiceProvider!.CreateScope())
        {
            var db = decideScope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var reloaded = (await decideScope.ServiceProvider.GetRequiredService<IApprovalRequestRepository>().GetByIdAsync(twoSteps.Id)).Value;
            reloaded.Decide(1, approver1, ApprovalOutcome.Rejected, null, false, ApprovalChannel.Web, null, null, DateTime.UtcNow).IsSuccess.ShouldBeTrue();
            await db.SaveChangesAsync();
        }

        using (var scope = ServiceProvider!.CreateScope())
        {
            var issuer = scope.ServiceProvider.GetRequiredService<IApprovalDecisionLinkIssuer>();
            (await issuer.IssueAsync(tenantId, twoSteps.Tasks.Single(t => t.StepNumber == 1).Id)).ShouldBeNull();
            (await scope.ServiceProvider.GetRequiredService<WorkflowDbContext>().EmailDecisionTokens.AsNoTracking().CountAsync()).ShouldBe(0);
        }
    }

    private async Task<ApprovalRequest> SubmitAsync(Guid tenantId, IReadOnlyList<ApprovalTaskSeed> steps)
    {
        using var scope = ServiceProvider!.CreateScope();
        var submitted = ApprovalRequest.Submit(
            tenantId, "Leave", Guid.NewGuid(), Guid.NewGuid(), steps, null, null, null, false, true, DateTime.UtcNow, null, null);
        submitted.IsSuccess.ShouldBeTrue();
        scope.ServiceProvider.GetRequiredService<IApprovalRequestRepository>().Add(submitted.Value);
        await scope.ServiceProvider.GetRequiredService<WorkflowDbContext>().SaveChangesAsync();
        return submitted.Value;
    }
}

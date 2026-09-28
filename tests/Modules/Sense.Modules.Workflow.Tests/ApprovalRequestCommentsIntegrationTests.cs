using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Workflow.Api;
using Sense.Modules.Workflow.Application;
using Sense.Modules.Workflow.Application.ApprovalRequests;
using Sense.Modules.Workflow.Domain;
using Sense.Modules.Workflow.Domain.ApprovalRequests;
using Sense.Modules.Workflow.Infrastructure.Persistence;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Context;
using Sense.Shared.Kernel.Results;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Workflow.Tests;

/// <summary>
/// FG-009 against real PostgreSQL with the whole Workflow module and the full pipeline (validation, UnitOfWork, outbox):
/// a comment is stored as a Commented history entry (string column - no migration), read back through the comments
/// query with the author's name, and enqueues ApprovalRequestCommented for the other parties; an employee who only
/// holds workflow.inbox.view gets 404 on the status read and on both comment operations.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class ApprovalRequestCommentsIntegrationTests : TestFixture
{
    private const string InboxViewPermission = "workflow.inbox.view";

    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    private readonly IEmployeeDirectory _employeeDirectory = Substitute.For<IEmployeeDirectory>();

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        new WorkflowModule().AddModule(services, configuration);

        _employeeDirectory.GetContactsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ((IReadOnlyCollection<Guid>)ci[1]).Select(id => new EmployeeContact(id, "Ayşe", "Yılmaz", null, null, null, "Active")).ToList());
        services.AddSingleton(_permissions);
        services.AddSingleton(_employeeDirectory);
        services.AddSingleton(Substitute.For<IDataScopeService>());
        services.AddSingleton(Substitute.For<IAccessSettingsService>());
        services.AddSingleton(Substitute.For<IRoleAssignmentLookup>());
        services.AddSingleton(Substitute.For<IEmployeeLookup>());
    }

    protected override async Task OnDatabaseReadyAsync() =>
        await ServiceProvider!.GetRequiredService<WorkflowDbContext>().Database.MigrateAsync();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private void ActAsEmployee(Guid employeeId, params string[] grantedPermissions)
    {
        var userId = Guid.NewGuid();
        foreach (var permission in grantedPermissions)
        {
            _permissions.HasAsync(userId, permission, Arg.Any<CancellationToken>()).Returns(true);
        }

        ((CurrentUserAccessor)CurrentUser!).Set(new AuthenticatedUser(
            UserId: userId, EmployeeId: employeeId, Email: "employee@test.local", DisplayName: "Employee",
            Roles: new HashSet<string>(StringComparer.Ordinal), IsPlatformAdmin: false,
            ImpersonatorUserId: null, ApiKeyId: null, CorrelationId: null, IpAddress: "127.0.0.1"));
    }

    private async Task<Guid> SeedPendingAsync(Guid tenantId, Guid requesterEmployeeId, Guid approverEmployeeId)
    {
        using var scope = ServiceProvider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var request = ApprovalRequest.Submit(
            tenantId, "Leave", Guid.NewGuid(), requesterEmployeeId, [new ApprovalTaskSeed(1, approverEmployeeId, null, true)],
            null, null, null, false, true, DateTime.UtcNow, null, null).Value;
        db.ApprovalRequests.Add(request);
        await db.SaveChangesAsync(Ct);
        return request.Id;
    }

    [Fact]
    public async Task RequesterComments_ApproverReadsIt_AndTheOutboxCarriesTheApproverAsRecipient()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var requester = Guid.NewGuid();
        var approver = Guid.NewGuid();
        var requestId = await SeedPendingAsync(tenantId, requester, approver);

        ActAsEmployee(requester, InboxViewPermission);
        var added = await Dispatcher!.Send(new AddApprovalRequestCommentCommand(requestId, requester, "Raporu ekledim"), Ct);
        added.IsSuccess.ShouldBeTrue();

        ActAsEmployee(approver, InboxViewPermission);
        var comments = await Dispatcher.Query(new ListApprovalRequestCommentsQuery(requestId), Ct);
        comments.IsSuccess.ShouldBeTrue();
        var comment = comments.Value.ShouldHaveSingleItem();
        comment.Id.ShouldBe(added.Value);
        comment.AuthorEmployeeId.ShouldBe(requester);
        comment.AuthorName.ShouldBe("Ayşe Yılmaz");
        comment.Text.ShouldBe("Raporu ekledim");

        var status = await Dispatcher.Query(new GetApprovalStatusQuery(requestId), Ct);
        status.IsSuccess.ShouldBeTrue();
        status.Value.Steps.ShouldHaveSingleItem().ApproverEmployeeId.ShouldBe(approver);

        using var verifyScope = ServiceProvider!.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var stored = await db.ApprovalRequests.AsNoTracking().SingleAsync(r => r.Id == requestId, Ct);
        stored.History.ShouldContain(h => h.EventType == ApprovalHistoryEventType.Commented && h.Note == "Raporu ekledim");
        var payload = (await db.OutboxMessages.AsNoTracking()
            .Where(m => m.Type.Contains("ApprovalRequestCommented"))
            .Select(m => m.Payload)
            .ToListAsync(Ct)).ShouldHaveSingleItem();
        payload.ShouldContain(approver.ToString());
        payload.ShouldContain(requestId.ToString());
    }

    [Fact]
    public async Task EmployeeWithOnlyInboxView_GetsNotFound_OnStatusAndComments_AndCannotComment()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var requestId = await SeedPendingAsync(tenantId, Guid.NewGuid(), Guid.NewGuid());
        var outsider = Guid.NewGuid();
        ActAsEmployee(outsider, InboxViewPermission);

        var status = await Dispatcher!.Query(new GetApprovalStatusQuery(requestId), Ct);
        var comments = await Dispatcher.Query(new ListApprovalRequestCommentsQuery(requestId), Ct);
        var added = await Dispatcher.Send(new AddApprovalRequestCommentCommand(requestId, outsider, "Merhaba"), Ct);

        foreach (var error in new[] { status.Error, comments.Error, added.Error })
        {
            error.Type.ShouldBe(ErrorType.NotFound);
            error.Code.ShouldBe(WorkflowErrors.RequestNotFound);
        }

        using var verifyScope = ServiceProvider!.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        (await db.ApprovalRequests.AsNoTracking().SingleAsync(r => r.Id == requestId, Ct))
            .History.ShouldNotContain(h => h.EventType == ApprovalHistoryEventType.Commented);
        (await db.OutboxMessages.AsNoTracking().AnyAsync(m => m.Type.Contains("ApprovalRequestCommented"), Ct)).ShouldBeFalse();
    }

    /// <summary>The validator rejects it before the handler runs (ValidationBehaviour → HTTP 400 general.validation_error).</summary>
    [Fact]
    public async Task CommentLongerThanTheLimit_IsRejectedByTheValidator()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var requester = Guid.NewGuid();
        var requestId = await SeedPendingAsync(tenantId, requester, Guid.NewGuid());
        ActAsEmployee(requester);

        await Should.ThrowAsync<FluentValidation.ValidationException>(() => Dispatcher!.Send(
            new AddApprovalRequestCommentCommand(requestId, requester, new string('a', WorkflowLimits.CommentMaxLength + 1)), Ct));
    }

    /// <summary>ApprovalHistoryEventType is stored as a string column, so appending Commented needs no migration.</summary>
    [Fact]
    public void WorkflowModel_HasNoPendingChanges_AfterAddingTheCommentedEventType()
    {
        using var scope = ServiceProvider!.CreateScope();
        scope.ServiceProvider.GetRequiredService<WorkflowDbContext>().Database.HasPendingModelChanges().ShouldBeFalse();
    }
}

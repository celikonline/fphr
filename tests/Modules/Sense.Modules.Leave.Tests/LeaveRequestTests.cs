using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Leave.Application;
using Sense.Modules.Leave.Application.Requests;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Ledger;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Modules.Leave.Infrastructure;
using Sense.Modules.Leave.Infrastructure.Persistence;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Workflow.Contracts;
using Sense.Modules.Workflow.Contracts.Events;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Kernel.Results;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Leave.Tests;

public sealed class LeaveRequestStateTests
{
    private static LeaveRequest NewRequest() =>
        LeaveRequest.Submit(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LeaveTypeCode.Annual, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 9), 5m,
            null, LeaveRequestSource.SelfService, DateTime.UtcNow, "10.0.0.1", null).Value;

    [Fact]
    public void Submit_RejectsInvertedRange()
    {
        LeaveRequest.Submit(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LeaveTypeCode.Annual, new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 5), 1m,
            null, LeaveRequestSource.SelfService, DateTime.UtcNow, null, null).Error.Code.ShouldBe(LeaveDomainErrors.RequestInvalidRange);
    }

    [Fact]
    public void UpdatePending_BumpsRevision_AndOldApprovalDecisionIsIgnored()
    {
        var request = NewRequest();
        var firstApproval = Guid.NewGuid();
        request.AttachApproval(firstApproval);

        request.UpdatePending(request.LeaveTypeId, LeaveTypeCode.Annual, new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 13), 2m, "kısaltıldı").IsSuccess.ShouldBeTrue();
        request.RevisionNo.ShouldBe(1);
        request.ApprovalRequestId.ShouldBeNull();
        var secondApproval = Guid.NewGuid();
        request.AttachApproval(secondApproval);

        request.Approve(firstApproval, DateTime.UtcNow).Error.Code.ShouldBe(LeaveDomainErrors.RequestApprovalMismatch);
        request.Status.ShouldBe(LeaveRequestStatus.Pending);
        request.Approve(secondApproval, DateTime.UtcNow).IsSuccess.ShouldBeTrue();
        request.Status.ShouldBe(LeaveRequestStatus.Approved);
        request.UpdatePending(request.LeaveTypeId, LeaveTypeCode.Annual, new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 13), 2m, null).Error.Code
            .ShouldBe(LeaveDomainErrors.RequestNotPending);
        request.Withdraw(DateTime.UtcNow).IsFailure.ShouldBeTrue();
    }
}

/// <summary>Submit → Workflow start (substituted IWorkflowService) → ApprovalCompleted/ApprovalRejected through IEventBus, on real PostgreSQL.</summary>
[Collection("PostgreSQL")]
public sealed class LeaveRequestIntegrationTests : TestFixture
{
    private readonly IWorkflowService _workflow = Substitute.For<IWorkflowService>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        services.AddModuleDbContext<LeaveDbContext>(configuration, LeaveDbContext.SchemaName);
        services.AddLeaveServices(configuration);
        services.AddModuleHandlers(
            LeaveDbContext.SchemaName, typeof(LeaveErrors).Assembly, typeof(LeavePermissions).Assembly, typeof(LeaveDbContext).Assembly, typeof(ILeaveRuleSetRepository).Assembly);

        var holidays = Substitute.For<IHolidayCalendarLookup>();
        holidays.ResolveCalendarAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns((HolidayCalendarRef?)null);
        services.AddSingleton(holidays);
    }

    protected override async Task OnDatabaseReadyAsync() =>
        await ServiceProvider!.GetRequiredService<LeaveDbContext>().Database.MigrateAsync();

    private async Task<Result<LeaveRequestDto>> SubmitAsync(Guid tenantId, Guid leaveTypeId, DateOnly start, DateOnly end)
    {
        using var scope = ServiceProvider!.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<SubmitLeaveRequestHandler>(scope.ServiceProvider, _currentUser, _workflow);
        var result = await handler.Handle(new SubmitLeaveRequestCommand(leaveTypeId, start, end, null), CancellationToken.None);
        if (result.IsSuccess)
        {
            await scope.ServiceProvider.GetRequiredService<LeaveDbContext>().SaveChangesAsync();
        }

        return result;
    }

    [Fact]
    public async Task Submit_Approve_PostsUsageAtStartDate_AndValidatesBalanceAndOverlap()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var accrualStart = today.AddYears(-1).AddDays(-10);
        _currentUser.EmployeeId.Returns(employeeId);
        var approvalId = Guid.NewGuid();
        _workflow.StartAsync(ApprovalRequestTypes.Leave, Arg.Any<Guid>(), employeeId, null, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(approvalId));
        var bus = ServiceProvider!.GetRequiredService<IEventBus>();
        await bus.Publish(new EmployeeHired(tenantId, employeeId, accrualStart, accrualStart));

        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        Guid annualId;
        using (var read = ServiceProvider.CreateScope())
        {
            annualId = (await read.ServiceProvider.GetRequiredService<LeaveDbContext>().RuleSets.SingleAsync()).Types.Single(t => t.Code == LeaveTypeCode.Annual).Id;
        }

        // 14 days accrued; Mon..Fri of a future week = 5 working days.
        var monday = today.AddDays(((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7 + 7);
        var submitted = await SubmitAsync(tenantId, annualId, monday, monday.AddDays(4));
        submitted.IsSuccess.ShouldBeTrue(submitted.IsFailure ? submitted.Error.Code : null);

        (await SubmitAsync(tenantId, annualId, monday.AddDays(2), monday.AddDays(3))).Error.Code.ShouldBe(LeaveRequestErrors.Overlap);
        (await SubmitAsync(tenantId, annualId, monday.AddDays(7), monday.AddDays(7 + 13))).Error.Code.ShouldBe(LeaveRequestErrors.InsufficientBalance);

        await bus.Publish(new ApprovalCompleted(tenantId, approvalId, ApprovalRequestTypes.Leave, submitted.Value.Id, employeeId));

        using var verify = ServiceProvider.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<LeaveDbContext>();
        var request = await db.Requests.SingleAsync(r => r.Id == submitted.Value.Id);
        request.Status.ShouldBe(LeaveRequestStatus.Approved);
        request.CalculatedDays.ShouldBe(5m);
        request.ApprovalRequestId.ShouldBe(approvalId);
        var usage = await db.LedgerEntries.SingleAsync(e => e.Type == LeaveLedgerEntryType.Usage);
        usage.Amount.ShouldBe(-5m);
        usage.EffectiveDate.ShouldBe(monday);
        usage.SourceRefId.ShouldBe(submitted.Value.Id);
    }

    [Fact]
    public async Task RequestableTypes_Preview_AndOnBehalfWithBypass_PostUsageWithoutWorkflow()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var accrualStart = today.AddYears(-1).AddDays(-10);
        _currentUser.EmployeeId.Returns(employeeId);
        var dataScope = Substitute.For<IDataScopeService>();
        dataScope.CanAccessEmployeeAsync(LeavePermissions.RequestsCreateOnBehalf.Key, employeeId, Arg.Any<CancellationToken>()).Returns(true);
        await ServiceProvider!.GetRequiredService<IEventBus>().Publish(new EmployeeHired(tenantId, employeeId, accrualStart, accrualStart));
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var monday = today.AddDays(((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7 + 7);

        using var scope = ServiceProvider.CreateScope();
        var types = await ActivatorUtilities.CreateInstance<ListMyRequestableLeaveTypesHandler>(scope.ServiceProvider, _currentUser, dataScope)
            .Handle(new ListMyRequestableLeaveTypesQuery(), CancellationToken.None);
        types.IsSuccess.ShouldBeTrue();
        types.Value.ShouldContain(t => t.Code == LeaveTypeCode.Annual && t.Name == "leave.type.annual" && t.HasBalance);
        var annualId = types.Value.Single(t => t.Code == LeaveTypeCode.Annual).Id;

        var preview = await ActivatorUtilities.CreateInstance<PreviewMyLeaveRequestHandler>(scope.ServiceProvider, _currentUser)
            .Handle(new PreviewMyLeaveRequestQuery(annualId, monday, monday.AddDays(6), null), CancellationToken.None);
        preview.Value.Days.ShouldBe(5m);
        preview.Value.WeeklyRestDays.ShouldBe(2);
        preview.Value.Warnings.ShouldBeEmpty();
        var tooLong = await ActivatorUtilities.CreateInstance<PreviewMyLeaveRequestHandler>(scope.ServiceProvider, _currentUser)
            .Handle(new PreviewMyLeaveRequestQuery(annualId, monday, monday.AddDays(27), null), CancellationToken.None);
        tooLong.Value.Warnings.Single().Code.ShouldBe(LeaveRequestErrors.InsufficientBalance);

        var onBehalf = ActivatorUtilities.CreateInstance<CreateLeaveRequestOnBehalfHandler>(scope.ServiceProvider, _currentUser, dataScope, _workflow);
        (await onBehalf.Handle(new CreateLeaveRequestOnBehalfCommand(employeeId, annualId, monday, monday.AddDays(1), null, true), CancellationToken.None))
            .Error.Code.ShouldBe(LeaveRequestErrors.BypassNotAllowed);
        dataScope.CanAccessEmployeeAsync(LeavePermissions.RequestsBypassApproval.Key, employeeId, Arg.Any<CancellationToken>()).Returns(true);
        var created = await onBehalf.Handle(new CreateLeaveRequestOnBehalfCommand(employeeId, annualId, monday, monday.AddDays(1), null, true), CancellationToken.None);
        created.IsSuccess.ShouldBeTrue(created.IsFailure ? created.Error.Code : null);
        await scope.ServiceProvider.GetRequiredService<LeaveDbContext>().SaveChangesAsync();
        await _workflow.DidNotReceiveWithAnyArgs().StartAsync(default!, default, default, default, default, default, default);

        using var verify = ServiceProvider.CreateScope();
        var mine = await ActivatorUtilities.CreateInstance<ListMyLeaveRequestsHandler>(verify.ServiceProvider, _currentUser)
            .Handle(new ListMyLeaveRequestsQuery(), CancellationToken.None);
        var request = mine.Value.Single();
        request.Status.ShouldBe(LeaveRequestStatus.Approved);
        request.Source.ShouldBe(LeaveRequestSource.OnBehalf);
        request.LeaveTypeName.ShouldBe("leave.type.annual");
        var ledger = await ActivatorUtilities.CreateInstance<Sense.Modules.Leave.Application.Balances.ListMyLeaveLedgerHandler>(verify.ServiceProvider, _currentUser)
            .Handle(new Sense.Modules.Leave.Application.Balances.ListMyLeaveLedgerQuery(annualId), CancellationToken.None);
        ledger.Value.ShouldContain(e => e.Type == LeaveLedgerEntryType.Usage && e.Amount == -2m && e.SourceRefId == created.Value.Id);
    }

    [Fact]
    public async Task ApprovalRejected_ForLeave_MarksRejected_AndIgnoresOtherRequestTypes()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();
        Guid requestId;
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var request = LeaveRequest.Submit(tenantId, employeeId, Guid.NewGuid(), LeaveTypeCode.Excuse, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 2), 1m,
                null, LeaveRequestSource.SelfService, DateTime.UtcNow, null, null).Value;
            request.AttachApproval(approvalId);
            db.Requests.Add(request);
            await db.SaveChangesAsync();
            requestId = request.Id;
        }

        var bus = ServiceProvider.GetRequiredService<IEventBus>();
        await bus.Publish(new ApprovalCompleted(tenantId, approvalId, ApprovalRequestTypes.Advance, requestId, employeeId));
        await bus.Publish(new ApprovalRejected(tenantId, approvalId, ApprovalRequestTypes.Leave, requestId, employeeId, 1));

        using var verify = ServiceProvider.CreateScope();
        var stored = await verify.ServiceProvider.GetRequiredService<LeaveDbContext>().Requests.SingleAsync(r => r.Id == requestId);
        stored.Status.ShouldBe(LeaveRequestStatus.Rejected);
        (await verify.ServiceProvider.GetRequiredService<LeaveDbContext>().LedgerEntries.CountAsync()).ShouldBe(0);
    }
}

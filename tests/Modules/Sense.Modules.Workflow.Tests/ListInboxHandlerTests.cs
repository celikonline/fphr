using Sense.Modules.Employee.Contracts;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Workflow.Application.ApprovalRequests;
using Sense.Modules.Workflow.Application.Delegations;
using Sense.Modules.Workflow.Contracts;
using Sense.Modules.Workflow.Domain;
using Sense.Modules.Workflow.Domain.ApprovalRequests;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Kernel.Results;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Sense.Modules.Workflow.Tests;

/// <summary>
/// Unit tests for ListInboxHandler with every port faked - no DB needed since the repository abstraction already
/// returns domain aggregates. Covers the InboxTaskDto.SubmittedAt contract added for the FE inbox date display
/// (see docs/implementation, faz-01 tur 2) and the InboxTaskDto.Summary projection added for senseik-047 (D-01: the
/// decide screen must show request-type-specific detail, not a raw SubjectId).
/// </summary>
public sealed class ListInboxHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly IApprovalRequestRepository _requests = Substitute.For<IApprovalRequestRepository>();
    private readonly IRoleAssignmentLookup _roleAssignments = Substitute.For<IRoleAssignmentLookup>();
    private readonly IEmployeeDirectory _employeeDirectory = Substitute.For<IEmployeeDirectory>();
    private readonly IApprovalDelegationRepository _delegations = Substitute.For<IApprovalDelegationRepository>();

    private ListInboxHandler HandlerWithProviders(params IApprovalRequestSummaryProvider[] providers) =>
        new(_currentUser, _tenant, _requests, _roleAssignments, _employeeDirectory, providers, new DelegationResolver(_delegations), TimeProvider.System,
            NSubstitute.Substitute.For<Sense.Modules.Identity.Contracts.ITenantTimeZoneLookup>());

    private ListInboxHandler Handler => HandlerWithProviders();

    [Fact]
    public async Task Handle_PendingTask_DtoSubmittedAtMatchesRequestSubmittedAt()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var submittedAt = new DateTime(2026, 9, 10, 8, 30, 0, DateTimeKind.Utc);

        _currentUser.EmployeeId.Returns(employeeId);
        _tenant.TenantId.Returns(tenantId);
        _roleAssignments.EmployeeHasRoleAsync(tenantId, employeeId, "HR", Arg.Any<CancellationToken>()).Returns(false);

        IReadOnlyList<ApprovalTaskSeed> steps = [new ApprovalTaskSeed(1, employeeId, null, true)];
        var request = ApprovalRequest.Submit(
            tenantId, "Leave", Guid.NewGuid(), requesterId, steps,
            null, null, null, false, true, submittedAt, null, null).Value;
        var task = request.Tasks.Single();

        _requests.ListInboxTasksAsync(employeeId, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([task]);
        _requests.GetByIdAsync(request.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(request)));
        _employeeDirectory.GetContactsAsync(tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await Handler.Handle(new ListInboxQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var dto = result.Value.ShouldHaveSingleItem();
        dto.SubmittedAt.ShouldBe(submittedAt);
    }

    [Fact]
    public async Task Handle_RequestTypeHasMatchingSummaryProvider_DtoSummaryComesFromProvider()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var submittedAt = new DateTime(2026, 9, 10, 8, 30, 0, DateTimeKind.Utc);
        var workDate = new DateOnly(2026, 9, 9);

        _currentUser.EmployeeId.Returns(employeeId);
        _tenant.TenantId.Returns(tenantId);
        _roleAssignments.EmployeeHasRoleAsync(tenantId, employeeId, "HR", Arg.Any<CancellationToken>()).Returns(false);

        IReadOnlyList<ApprovalTaskSeed> steps = [new ApprovalTaskSeed(1, employeeId, null, true)];
        var request = ApprovalRequest.Submit(
            tenantId, "Overtime", subjectId, requesterId, steps,
            null, null, null, false, true, submittedAt, null, null).Value;
        var task = request.Tasks.Single();

        _requests.ListInboxTasksAsync(employeeId, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([task]);
        _requests.GetByIdAsync(request.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(request)));
        _employeeDirectory.GetContactsAsync(tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var overtimeSummary = new ApprovalRequestSummary(workDate, Overtime: new OvertimeApprovalSummary(workDate, new TimeOnly(18, 0), 120, "Ay sonu kapanışı"));
        var overtimeProvider = Substitute.For<IApprovalRequestSummaryProvider>();
        overtimeProvider.RequestType.Returns("Overtime");
        overtimeProvider.GetSummaryAsync(tenantId, subjectId, Arg.Any<CancellationToken>()).Returns(overtimeSummary);
        var leaveProvider = Substitute.For<IApprovalRequestSummaryProvider>();
        leaveProvider.RequestType.Returns("Leave");

        var result = await HandlerWithProviders(overtimeProvider, leaveProvider).Handle(new ListInboxQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var dto = result.Value.ShouldHaveSingleItem();
        dto.Summary.ShouldBe(overtimeSummary);
        await leaveProvider.DidNotReceive().GetSummaryAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RequestTypeHasNoRegisteredProvider_DtoSummaryIsNull()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var submittedAt = new DateTime(2026, 9, 10, 8, 30, 0, DateTimeKind.Utc);

        _currentUser.EmployeeId.Returns(employeeId);
        _tenant.TenantId.Returns(tenantId);
        _roleAssignments.EmployeeHasRoleAsync(tenantId, employeeId, "HR", Arg.Any<CancellationToken>()).Returns(false);

        IReadOnlyList<ApprovalTaskSeed> steps = [new ApprovalTaskSeed(1, employeeId, null, true)];
        var request = ApprovalRequest.Submit(
            tenantId, "Advance", Guid.NewGuid(), requesterId, steps,
            null, null, null, false, true, submittedAt, null, null).Value;
        var task = request.Tasks.Single();

        _requests.ListInboxTasksAsync(employeeId, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([task]);
        _requests.GetByIdAsync(request.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(request)));
        _employeeDirectory.GetContactsAsync(tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await Handler.Handle(new ListInboxQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var dto = result.Value.ShouldHaveSingleItem();
        dto.Summary.ShouldBeNull();
    }
}

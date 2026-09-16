using Sense.Modules.Employee.Contracts;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Workflow.Application.ApprovalRequests;
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
/// (see docs/implementation, faz-01 tur 2): it must equal the parent ApprovalRequest's SubmittedAt.
/// </summary>
public sealed class ListInboxHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly IApprovalRequestRepository _requests = Substitute.For<IApprovalRequestRepository>();
    private readonly IRoleAssignmentLookup _roleAssignments = Substitute.For<IRoleAssignmentLookup>();
    private readonly IEmployeeDirectory _employeeDirectory = Substitute.For<IEmployeeDirectory>();

    private ListInboxHandler Handler => new(_currentUser, _tenant, _requests, _roleAssignments, _employeeDirectory);

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

        _requests.ListInboxTasksAsync(employeeId, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns([task]);
        _requests.GetByIdAsync(request.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(request)));
        _employeeDirectory.GetContactsAsync(tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await Handler.Handle(new ListInboxQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var dto = result.Value.ShouldHaveSingleItem();
        dto.SubmittedAt.ShouldBe(submittedAt);
    }
}

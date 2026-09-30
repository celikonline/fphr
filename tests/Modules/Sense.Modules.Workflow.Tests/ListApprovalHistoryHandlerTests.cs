using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Workflow.Application.ApprovalRequests;
using Sense.Modules.Workflow.Contracts;
using Sense.Modules.Workflow.Domain;
using Sense.Modules.Workflow.Domain.ApprovalRequests;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Context;
using Shouldly;
using Xunit;

namespace Sense.Modules.Workflow.Tests;

/// <summary>FG-008 approval history: caller-only source, paging/date arguments handed to the repository, DTO projection.</summary>
public sealed class ListApprovalHistoryHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly IApprovalRequestRepository _requests = Substitute.For<IApprovalRequestRepository>();
    private readonly IEmployeeDirectory _employeeDirectory = Substitute.For<IEmployeeDirectory>();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();

    public ListApprovalHistoryHandlerTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _currentUser.EmployeeId.Returns(_employeeId);
        _employeeDirectory.GetContactsAsync(_tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([]);
        _requests.ListDecidedTasksAsync(
                Arg.Any<Guid>(), Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new DecidedTaskPage([], 0));
    }

    private ListApprovalHistoryHandler Handler(params IApprovalRequestSummaryProvider[] providers) =>
        new(_currentUser, _tenant, _requests, _employeeDirectory, providers);

    [Fact]
    public async Task Handle_CallerWithoutEmployee_ReturnsEmptyPage_WithoutQuerying()
    {
        _currentUser.EmployeeId.Returns((Guid?)null);

        var result = await Handler().Handle(new ListApprovalHistoryQuery(null, null, null), TestContext.Current.CancellationToken);

        result.Value.Items.ShouldBeEmpty();
        result.Value.TotalCount.ShouldBe(0);
        await _requests.DidNotReceiveWithAnyArgs().ListDecidedTasksAsync(default, default, default, default, default, default, default);
    }

    [Fact]
    public async Task Handle_PassesCallerInclusiveUtcDayRangeTypeAndPaging()
    {
        await Handler().Handle(
            new ListApprovalHistoryQuery(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), " Leave ", Page: 3, PageSize: 10),
            TestContext.Current.CancellationToken);

        await _requests.Received(1).ListDecidedTasksAsync(
            _employeeId,
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            "Leave",
            20,
            10,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0, 0, 0, PagingDefaults.DefaultPageSize)]
    [InlineData(-5, 1000, 0, PagingDefaults.MaxPageSize)]
    public async Task Handle_ClampsPaging(int page, int pageSize, int expectedSkip, int expectedTake)
    {
        var result = await Handler().Handle(new ListApprovalHistoryQuery(null, null, null, page, pageSize), TestContext.Current.CancellationToken);

        result.Value.Page.ShouldBe(PagingDefaults.FirstPage);
        result.Value.PageSize.ShouldBe(expectedTake);
        await _requests.Received(1).ListDecidedTasksAsync(_employeeId, null, null, null, expectedSkip, expectedTake, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ProjectsOutcomeRequestStatusRequesterNameAndSummary()
    {
        var requesterId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var decidedAt = new DateTime(2026, 9, 20, 9, 15, 0, DateTimeKind.Utc);
        var approved = new DecidedTaskRow(
            Guid.NewGuid(), Guid.NewGuid(), "Overtime", subjectId, requesterId, 1, ApprovalTaskStatus.Approved, decidedAt, null, ApprovalRequestStatus.Pending);
        var rejected = new DecidedTaskRow(
            Guid.NewGuid(), Guid.NewGuid(), "Advance", Guid.NewGuid(), requesterId, 2, ApprovalTaskStatus.Rejected, decidedAt.AddHours(-1), "Uygun değil",
            ApprovalRequestStatus.Rejected);
        _requests.ListDecidedTasksAsync(_employeeId, null, null, null, 0, PagingDefaults.DefaultPageSize, Arg.Any<CancellationToken>())
            .Returns(new DecidedTaskPage([approved, rejected], 7));
        _employeeDirectory.GetContactsAsync(_tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new EmployeeContact(requesterId, "Ayşe", "Yılmaz", null, null, null, "Active")]);
        var summary = new ApprovalRequestSummary(new DateOnly(2026, 9, 19));
        var overtimeProvider = Substitute.For<IApprovalRequestSummaryProvider>();
        overtimeProvider.RequestType.Returns("Overtime");
        overtimeProvider.GetSummaryAsync(_tenantId, subjectId, Arg.Any<CancellationToken>()).Returns(summary);

        var result = await Handler(overtimeProvider).Handle(new ListApprovalHistoryQuery(null, null, null), TestContext.Current.CancellationToken);

        result.Value.TotalCount.ShouldBe(7);
        var first = result.Value.Items[0];
        first.TaskId.ShouldBe(approved.TaskId);
        first.ApprovalRequestId.ShouldBe(approved.ApprovalRequestId);
        first.Outcome.ShouldBe(ApprovalOutcome.Approved);
        first.RequestStatus.ShouldBe(ApprovalRequestStatus.Pending);
        first.RequesterName.ShouldBe("Ayşe Yılmaz");
        first.DecidedAt.ShouldBe(decidedAt);
        first.Summary.ShouldBe(summary);
        var second = result.Value.Items[1];
        second.Outcome.ShouldBe(ApprovalOutcome.Rejected);
        second.Comment.ShouldBe("Uygun değil");
        second.StepNumber.ShouldBe(2);
        second.Summary.ShouldBeNull();
    }
}

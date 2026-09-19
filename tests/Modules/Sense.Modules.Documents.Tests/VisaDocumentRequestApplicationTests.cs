using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Sense.Modules.Documents.Application;
using Sense.Modules.Documents.Application.VisaDocuments;
using Sense.Modules.Documents.Domain.VisaDocuments;
using Sense.Modules.Workflow.Contracts;
using Sense.Modules.Workflow.Contracts.Events;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Documents.Tests;

public sealed class VisaDocumentRequestApplicationTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid ApproverEmployeeId = Guid.NewGuid();

    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IVisaDocumentRequestRepository _requests = Substitute.For<IVisaDocumentRequestRepository>();
    private readonly IVisaDocumentProcessRepository _processes = Substitute.For<IVisaDocumentProcessRepository>();
    private readonly IWorkflowService _workflow = Substitute.For<IWorkflowService>();
    private readonly IVisaSettingsProvider _settings = Substitute.For<IVisaSettingsProvider>();
    private readonly IVisaDocumentDeliveryService _delivery = Substitute.For<IVisaDocumentDeliveryService>();
    private readonly IDocumentsUnitOfWork _unitOfWork = Substitute.For<IDocumentsUnitOfWork>();

    public VisaDocumentRequestApplicationTests()
    {
        _tenant.TenantId.Returns(TenantId);
        _currentUser.EmployeeId.Returns(EmployeeId);
    }

    private SubmitVisaDocumentRequestHandler SubmitHandler() =>
        new(_tenant, _currentUser, _requests, _processes, _workflow, _settings, NullLogger<SubmitVisaDocumentRequestHandler>.Instance);

    private VisaApprovalCompletedHandler CompletedHandler() =>
        new(_requests, _delivery, _unitOfWork, NullLogger<VisaApprovalCompletedHandler>.Instance);

    private VisaApprovalRejectedHandler RejectedHandler() => new(_requests, _unitOfWork);

    private static SubmitVisaDocumentRequestCommand SubmitCommand(Guid? processId = null) => new(
        VisaDocumentProcessId: processId,
        TravelStart: new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
        TravelEnd: new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc),
        CountryCode: "de",
        VisaType: VisaTypes.Business,
        Description: "Berlin musteri ziyareti");

    private static VisaDocumentRequest AwaitingApprovalRequest()
    {
        var request = VisaDocumentRequest.Create(
            tenantId: TenantId,
            employeeId: EmployeeId,
            visaDocumentProcessId: null,
            travelStart: new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
            travelEnd: new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc),
            countryCode: "DE",
            visaType: VisaTypes.Business,
            description: "Berlin musteri ziyareti").Value;

        request.SubmitForApproval(Guid.NewGuid());
        return request;
    }

    private void WorkflowStartsWith(Result<Guid> result) =>
        _workflow.StartAsync(
                Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyList<ApprovalStepInput>?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(result);

    private static ApprovalCompleted Completed(Guid subjectId, string requestType = SubmitVisaDocumentRequestHandler.RequestType) =>
        new(TenantId, Guid.NewGuid(), requestType, subjectId, EmployeeId);

    private static ApprovalRejected Rejected(Guid subjectId, string requestType = SubmitVisaDocumentRequestHandler.RequestType) =>
        new(TenantId, Guid.NewGuid(), requestType, subjectId, EmployeeId, RejectedAtStep: 1);

    [Fact]
    public async Task Submit_WithConfiguredApprover_StartsSingleStepApprovalAndPersists()
    {
        var approvalRequestId = Guid.NewGuid();
        _settings.GetApproverEmployeeIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(ApproverEmployeeId);
        WorkflowStartsWith(Result.Success(approvalRequestId));

        var result = await SubmitHandler().Handle(SubmitCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(VisaDocumentRequestStatus.AwaitingApproval);
        result.Value.ApprovalRequestId.ShouldBe(approvalRequestId);

        await _requests.Received(1).AddAsync(
            Arg.Is<VisaDocumentRequest>(r => r.EmployeeId == EmployeeId && r.CountryCode == "DE"), Arg.Any<CancellationToken>());
        await _requests.Received(1).SaveAsync(Arg.Any<CancellationToken>());

        await _workflow.Received(1).StartAsync(
            SubmitVisaDocumentRequestHandler.RequestType,
            Arg.Any<Guid>(),
            EmployeeId,
            Arg.Is<IReadOnlyList<ApprovalStepInput>?>(steps =>
                steps != null && steps.Count == 1 && steps[0].ApproverEmployeeId == ApproverEmployeeId && steps[0].IsRequired),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Submit_WithoutLinkedEmployee_Fails()
    {
        _currentUser.EmployeeId.Returns((Guid?)null);

        var result = await SubmitHandler().Handle(SubmitCommand(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(VisaDomainErrors.VisaDocumentRequestNoEmployeeForUser);
        await _requests.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Submit_WithoutConfiguredApprover_FailsAndDoesNotStartWorkflow()
    {
        _settings.GetApproverEmployeeIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns((Guid?)null);

        var result = await SubmitHandler().Handle(SubmitCommand(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(VisaDomainErrors.VisaDocumentRequestApproverNotConfigured);

        await _workflow.DidNotReceiveWithAnyArgs().StartAsync(default!, default, default, default, default, default, default);
        await _requests.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Submit_WithInactiveProcess_FailsBeforeStartingWorkflow()
    {
        var process = VisaDocumentProcess.Create(TenantId, "Schengen").Value;
        process.Deactivate();
        _settings.GetApproverEmployeeIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(ApproverEmployeeId);
        _processes.GetByIdAsync(process.Id, Arg.Any<CancellationToken>()).Returns(process);

        var result = await SubmitHandler().Handle(SubmitCommand(process.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(VisaDomainErrors.VisaDocumentRequestProcessUnavailable);
        await _workflow.DidNotReceiveWithAnyArgs().StartAsync(default!, default, default, default, default, default, default);
    }

    [Fact]
    public async Task Submit_WhenWorkflowStartFails_DoesNotPersistRequest()
    {
        _settings.GetApproverEmployeeIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(ApproverEmployeeId);
        WorkflowStartsWith(Result.Failure<Guid>(Error.Failure("workflow.no_approver_resolved")));

        var result = await SubmitHandler().Handle(SubmitCommand(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        await _requests.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _requests.DidNotReceiveWithAnyArgs().SaveAsync(default);
    }

    [Theory]
    [InlineData("tourist", true)]
    [InlineData("business", true)]
    [InlineData("Business", false)]
    [InlineData("diplomatic", false)]
    [InlineData("", false)]
    public void SubmitValidator_OnlyAcceptsSeededVisaTypes(string visaType, bool valid)
    {
        var command = SubmitCommand() with { VisaType = visaType };

        new SubmitVisaDocumentRequestValidator().Validate(command).IsValid.ShouldBe(valid);
    }

    [Theory]
    [InlineData("DE", true)]
    [InlineData("deu", true)]
    [InlineData("D", false)]
    [InlineData("D3", false)]
    [InlineData("DEUT", false)]
    public void SubmitValidator_RequiresATwoOrThreeLetterCountryCode(string countryCode, bool valid)
    {
        var command = SubmitCommand() with { CountryCode = countryCode };

        new SubmitVisaDocumentRequestValidator().Validate(command).IsValid.ShouldBe(valid);
    }

    [Fact]
    public async Task ApprovalCompleted_DeliversDocumentsAndCompletesRequest()
    {
        var request = AwaitingApprovalRequest();
        _requests.GetByIdAsync(request.Id, Arg.Any<CancellationToken>()).Returns(request);
        _delivery.DeliverVisaDocumentsAsync(TenantId, EmployeeId, request.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Success(2));

        await CompletedHandler().Handle(Completed(request.Id), CancellationToken.None);

        request.Status.ShouldBe(VisaDocumentRequestStatus.Completed);
        request.ApprovedAt.ShouldNotBeNull();
        request.CompletedAt.ShouldNotBeNull();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApprovalCompleted_WhenDeliveryFails_ThrowsForRetryAndLeavesRequestUntouched()
    {
        var request = AwaitingApprovalRequest();
        _requests.GetByIdAsync(request.Id, Arg.Any<CancellationToken>()).Returns(request);
        _delivery.DeliverVisaDocumentsAsync(TenantId, EmployeeId, request.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<int>(Error.Failure("storage.unavailable")));

        await Should.ThrowAsync<InvalidOperationException>(() => CompletedHandler().Handle(Completed(request.Id), CancellationToken.None));

        request.Status.ShouldBe(VisaDocumentRequestStatus.AwaitingApproval);
        request.ApprovedAt.ShouldBeNull();
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task ApprovalCompleted_ForAnotherRequestType_IsIgnored()
    {
        await CompletedHandler().Handle(Completed(Guid.NewGuid(), requestType: "Leave"), CancellationToken.None);

        await _requests.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
        await _delivery.DidNotReceiveWithAnyArgs().DeliverVisaDocumentsAsync(default, default, default, default);
    }

    [Fact]
    public async Task ApprovalCompleted_RedeliveredForAlreadyCompletedRequest_IsIdempotent()
    {
        var request = AwaitingApprovalRequest();
        request.Approve();
        request.Complete();
        _requests.GetByIdAsync(request.Id, Arg.Any<CancellationToken>()).Returns(request);

        await CompletedHandler().Handle(Completed(request.Id), CancellationToken.None);

        await _delivery.DidNotReceiveWithAnyArgs().DeliverVisaDocumentsAsync(default, default, default, default);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task ApprovalRejected_MarksRequestRejectedWithoutDeliveringDocuments()
    {
        var request = AwaitingApprovalRequest();
        _requests.GetByIdAsync(request.Id, Arg.Any<CancellationToken>()).Returns(request);

        await RejectedHandler().Handle(Rejected(request.Id), CancellationToken.None);

        request.Status.ShouldBe(VisaDocumentRequestStatus.Rejected);
        request.RejectedAt.ShouldNotBeNull();
        request.RejectionReason.ShouldBeNull();
        await _delivery.DidNotReceiveWithAnyArgs().DeliverVisaDocumentsAsync(default, default, default, default);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApprovalRejected_ForAnotherRequestType_IsIgnored()
    {
        await RejectedHandler().Handle(Rejected(Guid.NewGuid(), requestType: "Overtime"), CancellationToken.None);

        await _requests.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    [Fact]
    public async Task Get_ForAnotherEmployeesRequest_IsNotFound()
    {
        var request = AwaitingApprovalRequest();
        _requests.GetByIdAsync(request.Id, Arg.Any<CancellationToken>()).Returns(request);
        _currentUser.EmployeeId.Returns(Guid.NewGuid());

        var result = await new GetVisaDocumentRequestHandler(_requests, _currentUser)
            .Handle(new GetVisaDocumentRequestQuery(request.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(VisaDomainErrors.VisaDocumentRequestNotFound);
    }

    [Fact]
    public async Task Get_ForOwnRequest_ReturnsIt()
    {
        var request = AwaitingApprovalRequest();
        _requests.GetByIdAsync(request.Id, Arg.Any<CancellationToken>()).Returns(request);

        var result = await new GetVisaDocumentRequestHandler(_requests, _currentUser)
            .Handle(new GetVisaDocumentRequestQuery(request.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Id.ShouldBe(request.Id);
    }

    [Fact]
    public async Task List_ReturnsOnlyTheCallersRequests()
    {
        var mine = AwaitingApprovalRequest();
        _requests.GetByEmployeeIdAsync(TenantId, EmployeeId, null, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<VisaDocumentRequest>)[mine]);

        var result = await new ListMyVisaDocumentRequestsHandler(_requests, _tenant, _currentUser)
            .Handle(new ListMyVisaDocumentRequestsQuery(null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(1);
        await _requests.Received(1).GetByEmployeeIdAsync(TenantId, EmployeeId, null, Arg.Any<CancellationToken>());
    }
}

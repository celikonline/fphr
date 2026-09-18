using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Sense.Modules.Documents.Application.VisaDocuments;
using Sense.Modules.Documents.Domain.VisaDocuments;
using Sense.Modules.Workflow.Contracts;
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
    private readonly IVisaDocumentRequestRepository _requests = Substitute.For<IVisaDocumentRequestRepository>();
    private readonly IWorkflowService _workflow = Substitute.For<IWorkflowService>();
    private readonly IVisaSettingsProvider _settings = Substitute.For<IVisaSettingsProvider>();
    private readonly IVisaDocumentDeliveryService _delivery = Substitute.For<IVisaDocumentDeliveryService>();

    public VisaDocumentRequestApplicationTests()
    {
        _tenant.TenantId.Returns(TenantId);
    }

    private SubmitVisaDocumentRequestHandler SubmitHandler() =>
        new(_tenant, _requests, _workflow, _settings, NullLogger<SubmitVisaDocumentRequestHandler>.Instance);

    private static SubmitVisaDocumentRequestCommand SubmitCommand() => new(
        EmployeeId: EmployeeId,
        VisaDocumentProcessId: null,
        TravelStart: new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
        TravelEnd: new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc),
        CountryCode: "DE",
        VisaType: "Business",
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
            visaType: "Business",
            description: "Berlin musteri ziyareti").Value;

        request.SubmitForApproval(Guid.NewGuid());
        return request;
    }

    [Fact]
    public async Task Submit_WithConfiguredApprover_StartsSingleStepApprovalAndPersists()
    {
        var approvalRequestId = Guid.NewGuid();
        _settings.GetApproverEmployeeIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(ApproverEmployeeId);
        _workflow.StartAsync(
                Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyList<ApprovalStepInput>?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(approvalRequestId));

        var result = await SubmitHandler().Handle(SubmitCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(VisaDocumentRequestStatus.AwaitingApproval);
        result.Value.ApprovalRequestId.ShouldBe(approvalRequestId);

        await _requests.Received(1).AddAsync(Arg.Any<VisaDocumentRequest>(), Arg.Any<CancellationToken>());
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
    public async Task Submit_WhenWorkflowStartFails_DoesNotPersistRequest()
    {
        _settings.GetApproverEmployeeIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(ApproverEmployeeId);
        _workflow.StartAsync(
                Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyList<ApprovalStepInput>?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<Guid>(Error.Failure("workflow.no_approver_resolved")));

        var result = await SubmitHandler().Handle(SubmitCommand(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        await _requests.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _requests.DidNotReceiveWithAnyArgs().SaveAsync(default);
    }

    [Fact]
    public async Task Approve_DeliversDocumentsAndCompletesRequest()
    {
        var request = AwaitingApprovalRequest();
        _requests.GetByIdAsync(request.Id, Arg.Any<CancellationToken>()).Returns(request);
        _delivery.DeliverVisaDocumentsAsync(TenantId, EmployeeId, request.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Success(2));

        var handler = new ApproveVisaDocumentRequestHandler(
            _tenant, _requests, _delivery, NullLogger<ApproveVisaDocumentRequestHandler>.Instance);

        var result = await handler.Handle(new ApproveVisaDocumentRequestCommand(request.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(VisaDocumentRequestStatus.Completed);
        result.Value.DocumentsDelivered.ShouldBe(2);
        request.ApprovedAt.ShouldNotBeNull();
        request.CompletedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Approve_WhenDeliveryFails_LeavesRequestUncompleted()
    {
        var request = AwaitingApprovalRequest();
        _requests.GetByIdAsync(request.Id, Arg.Any<CancellationToken>()).Returns(request);
        _delivery.DeliverVisaDocumentsAsync(TenantId, EmployeeId, request.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<int>(Error.Failure("storage.unavailable")));

        var handler = new ApproveVisaDocumentRequestHandler(
            _tenant, _requests, _delivery, NullLogger<ApproveVisaDocumentRequestHandler>.Instance);

        var result = await handler.Handle(new ApproveVisaDocumentRequestCommand(request.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        request.Status.ShouldBe(VisaDocumentRequestStatus.Approved);
        request.CompletedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Approve_ForRequestOfAnotherTenant_IsRejected()
    {
        var request = AwaitingApprovalRequest();
        _tenant.TenantId.Returns(Guid.NewGuid()); // different tenant than the request
        _requests.GetByIdAsync(request.Id, Arg.Any<CancellationToken>()).Returns(request);

        var handler = new ApproveVisaDocumentRequestHandler(
            _tenant, _requests, _delivery, NullLogger<ApproveVisaDocumentRequestHandler>.Instance);

        var result = await handler.Handle(new ApproveVisaDocumentRequestCommand(request.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        await _delivery.DidNotReceiveWithAnyArgs().DeliverVisaDocumentsAsync(default, default, default, default);
    }

    [Fact]
    public async Task Reject_RecordsReasonAndStatus()
    {
        var request = AwaitingApprovalRequest();
        _requests.GetByIdAsync(request.Id, Arg.Any<CancellationToken>()).Returns(request);

        var handler = new RejectVisaDocumentRequestHandler(
            _tenant, _requests, NullLogger<RejectVisaDocumentRequestHandler>.Instance);

        var result = await handler.Handle(
            new RejectVisaDocumentRequestCommand(request.Id, "Pasaport suresi yetersiz", ApproverEmployeeId),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(VisaDocumentRequestStatus.Rejected);
        result.Value.RejectionReason.ShouldBe("Pasaport suresi yetersiz");
        request.RejectedBy.ShouldBe(ApproverEmployeeId);
    }
}

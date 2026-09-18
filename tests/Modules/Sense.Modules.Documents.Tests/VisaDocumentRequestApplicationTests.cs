using Moq;
using Sense.Modules.Documents.Application.VisaDocuments;
using Sense.Modules.Documents.Domain.VisaDocuments;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Contracts.Workflow;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Documents.Tests;

public sealed class VisaDocumentRequestApplicationTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid ApproverId = Guid.NewGuid();

    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IVisaDocumentRequestRepository> _visaRequestsRepo = new();
    private readonly Mock<IWorkflowService> _workflowService = new();
    private readonly Mock<ITenantSettings> _tenantSettings = new();
    private readonly Mock<IDocumentDeliveryService> _documentDelivery = new();
    private readonly Mock<ILogger<SubmitVisaDocumentRequestHandler>> _logger = new();

    public VisaDocumentRequestApplicationTests()
    {
        _tenantContext.SetupGet(x => x.Id).Returns(TenantId);
        _currentUser.SetupGet(x => x.Id).Returns(EmployeeId);
    }

    [Fact]
    public async Task SubmitVisaDocumentRequest_WithValidCommand_CreatesRequestAndInitiatesApproval()
    {
        // Arrange
        var command = new SubmitVisaDocumentRequestCommand(
            EmployeeId: EmployeeId,
            VisaDocumentProcessId: Guid.NewGuid(),
            TravelStart: DateTime.UtcNow.AddDays(30),
            TravelEnd: DateTime.UtcNow.AddDays(37),
            CountryCode: "TR",
            VisaType: "Business",
            Description: "Business trip to Istanbul");

        var approvalRequestId = Guid.NewGuid();
        var workflowResponse = new WorkflowApprovalResponse(approvalRequestId, VisaDocumentRequestStatus.AwaitingApproval);

        _tenantSettings
            .Setup(x => x.GetVisaApproverAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApproverId);

        _workflowService
            .Setup(x => x.StartAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid[]>(),
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(workflowResponse));

        var handler = new SubmitVisaDocumentRequestHandler(
            _tenantContext.Object,
            _currentUser.Object,
            _visaRequestsRepo.Object,
            _workflowService.Object,
            _tenantSettings.Object,
            _logger.Object);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(VisaDocumentRequestStatus.AwaitingApproval);
        result.Value.ApprovalRequestId.ShouldBe(approvalRequestId);

        _visaRequestsRepo.Verify(x => x.AddAsync(It.IsAny<VisaDocumentRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _visaRequestsRepo.Verify(x => x.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);
        _workflowService.Verify(x => x.StartAsync(
            It.Is<string>(s => s == "VisaDocument"),
            It.IsAny<Guid>(),
            It.Is<Guid>(g => g == EmployeeId),
            It.Is<Guid[]>(arr => arr.Contains(ApproverId)),
            It.IsAny<Dictionary<string, object>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitVisaDocumentRequest_WithoutConfiguredApprover_FailsWithError()
    {
        // Arrange
        var command = new SubmitVisaDocumentRequestCommand(
            EmployeeId: EmployeeId,
            VisaDocumentProcessId: null,
            TravelStart: DateTime.UtcNow.AddDays(30),
            TravelEnd: DateTime.UtcNow.AddDays(37),
            CountryCode: "TR",
            VisaType: "Business",
            Description: "Test");

        _tenantSettings
            .Setup(x => x.GetVisaApproverAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.Empty); // No approver configured

        var handler = new SubmitVisaDocumentRequestHandler(
            _tenantContext.Object,
            _currentUser.Object,
            _visaRequestsRepo.Object,
            _workflowService.Object,
            _tenantSettings.Object,
            _logger.Object);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(VisaDomainErrors.VisaDocumentRequestApproverNotConfigured);
    }

    [Fact]
    public async Task ApproveVisaDocumentRequest_WithValidRequest_ApprovesAndDeliversDocuments()
    {
        // Arrange
        var requestId = Guid.NewGuid();
        var request = VisaDocumentRequest.Create(
            tenantId: TenantId,
            employeeId: EmployeeId,
            visaDocumentProcessId: null,
            travelStart: DateTime.UtcNow.AddDays(30),
            travelEnd: DateTime.UtcNow.AddDays(37),
            countryCode: "TR",
            visaType: "Business",
            description: "Test").Value;

        // Set the ID for testing
        var approvalRequestId = Guid.NewGuid();
        request.SubmitForApproval(approvalRequestId);

        var command = new ApproveVisaDocumentRequestCommand(requestId);

        _visaRequestsRepo
            .Setup(x => x.GetByIdAsync(requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        _documentDelivery
            .Setup(x => x.DeliverVisaDocumentsAsync(
                TenantId,
                EmployeeId,
                requestId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(2)); // 2 documents delivered

        var handler = new ApproveVisaDocumentRequestHandler(
            _tenantContext.Object,
            _visaRequestsRepo.Object,
            _documentDelivery.Object,
            new Mock<ILogger<ApproveVisaDocumentRequestHandler>>().Object);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(VisaDocumentRequestStatus.Completed);
        result.Value.DocumentsDelivered.ShouldBe(2);

        _documentDelivery.Verify(
            x => x.DeliverVisaDocumentsAsync(TenantId, EmployeeId, requestId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RejectVisaDocumentRequest_WithValidCommand_RejectsAndSetsReason()
    {
        // Arrange
        var requestId = Guid.NewGuid();
        var request = VisaDocumentRequest.Create(
            tenantId: TenantId,
            employeeId: EmployeeId,
            visaDocumentProcessId: null,
            travelStart: DateTime.UtcNow.AddDays(30),
            travelEnd: DateTime.UtcNow.AddDays(37),
            countryCode: "TR",
            visaType: "Business",
            description: "Test").Value;

        var approvalRequestId = Guid.NewGuid();
        request.SubmitForApproval(approvalRequestId);

        var command = new RejectVisaDocumentRequestCommand(
            RequestId: requestId,
            RejectionReason: "Missing required documents",
            RejectedBy: ApproverId);

        _visaRequestsRepo
            .Setup(x => x.GetByIdAsync(requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        var handler = new RejectVisaDocumentRequestHandler(
            _tenantContext.Object,
            _currentUser.Object,
            _visaRequestsRepo.Object,
            new Mock<ILogger<RejectVisaDocumentRequestHandler>>().Object);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(VisaDocumentRequestStatus.Rejected);
        result.Value.RejectionReason.ShouldBe("Missing required documents");
    }
}

/// <summary>Mock workflow approval response for testing.</summary>
public sealed record WorkflowApprovalResponse(Guid Id, VisaDocumentRequestStatus Status);

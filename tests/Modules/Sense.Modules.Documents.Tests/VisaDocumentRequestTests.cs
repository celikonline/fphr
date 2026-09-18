using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.VisaDocuments;
using Shouldly;
using Xunit;

namespace Sense.Modules.Documents.Tests;

public sealed class VisaDocumentRequestTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();

    [Fact]
    public void Create_WithValidData_InitializesRequestInPendingStatus()
    {
        var processId = Guid.NewGuid();
        var travelStart = DateTime.UtcNow.AddDays(30);
        var travelEnd = travelStart.AddDays(7);
        var countryCode = "TR";
        var visaType = "Business";
        var description = "Business travel visa request";

        var request = VisaDocumentRequest.Create(
            tenantId: TenantId,
            employeeId: EmployeeId,
            visaDocumentProcessId: processId,
            travelStart: travelStart,
            travelEnd: travelEnd,
            countryCode: countryCode,
            visaType: visaType,
            description: description
        ).Value;

        request.EmployeeId.ShouldBe(EmployeeId);
        request.VisaDocumentProcessId.ShouldBe(processId);
        request.TravelStart.ShouldBe(travelStart);
        request.TravelEnd.ShouldBe(travelEnd);
        request.CountryCode.ShouldBe(countryCode);
        request.VisaType.ShouldBe(visaType);
        request.Description.ShouldBe(description);
        request.Status.ShouldBe(VisaDocumentRequestStatus.Pending);
        request.ApprovalRequestId.ShouldBeNull();
    }

    [Fact]
    public void Create_WithInvalidTravelDates_Fails()
    {
        var processId = Guid.NewGuid();
        var travelStart = DateTime.UtcNow.AddDays(30);
        var travelEnd = travelStart.AddDays(-1); // End before start

        var result = VisaDocumentRequest.Create(
            tenantId: TenantId,
            employeeId: EmployeeId,
            visaDocumentProcessId: processId,
            travelStart: travelStart,
            travelEnd: travelEnd,
            countryCode: "TR",
            visaType: "Business",
            description: "Test"
        );

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void SubmitForApproval_TransitionsToAwaitingApprovalAndSetsApprovalRequestId()
    {
        var request = VisaDocumentRequest.Create(
            tenantId: TenantId,
            employeeId: EmployeeId,
            visaDocumentProcessId: Guid.NewGuid(),
            travelStart: DateTime.UtcNow.AddDays(30),
            travelEnd: DateTime.UtcNow.AddDays(37),
            countryCode: "TR",
            visaType: "Business",
            description: "Test"
        ).Value;

        var approvalRequestId = Guid.NewGuid();
        request.SubmitForApproval(approvalRequestId);

        request.Status.ShouldBe(VisaDocumentRequestStatus.AwaitingApproval);
        request.ApprovalRequestId.ShouldBe(approvalRequestId);
    }

    [Fact]
    public void Approve_TransitionsToApproved()
    {
        var request = VisaDocumentRequest.Create(
            tenantId: TenantId,
            employeeId: EmployeeId,
            visaDocumentProcessId: Guid.NewGuid(),
            travelStart: DateTime.UtcNow.AddDays(30),
            travelEnd: DateTime.UtcNow.AddDays(37),
            countryCode: "TR",
            visaType: "Business",
            description: "Test"
        ).Value;

        var approvalRequestId = Guid.NewGuid();
        request.SubmitForApproval(approvalRequestId);
        request.Approve();

        request.Status.ShouldBe(VisaDocumentRequestStatus.Approved);
    }

    [Fact]
    public void Reject_WithReason_TransitionsToRejected()
    {
        var request = VisaDocumentRequest.Create(
            tenantId: TenantId,
            employeeId: EmployeeId,
            visaDocumentProcessId: Guid.NewGuid(),
            travelStart: DateTime.UtcNow.AddDays(30),
            travelEnd: DateTime.UtcNow.AddDays(37),
            countryCode: "TR",
            visaType: "Business",
            description: "Test"
        ).Value;

        var approvalRequestId = Guid.NewGuid();
        request.SubmitForApproval(approvalRequestId);
        request.Reject("Missing required documents");

        request.Status.ShouldBe(VisaDocumentRequestStatus.Rejected);
        request.RejectionReason.ShouldBe("Missing required documents");
    }
}

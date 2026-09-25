using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.PunchRequests;
using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Workflow.Contracts.Events;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Attendance.Application.PunchRequests;

/// <summary>
/// Workflow approved an "AttendancePunch" request: the punch is recorded through <see cref="IPunchRecorder"/> (Source = Request,
/// SourceRef = request id), the day is recalculated and the request becomes Approved. If the punch can no longer be recorded
/// (e.g. the month was closed while the request was pending) the request is rejected instead of staying pending forever.
/// </summary>
public sealed class PunchRequestApprovalCompletedHandler(
    IPunchRequestRepository requests, IPunchRecorder recorder, IAttendanceDayRecalculator recalculator, IAttendanceUnitOfWork unitOfWork, TimeProvider clock)
    : IIntegrationEventHandler<ApprovalCompleted>
{
    public async Task Handle(ApprovalCompleted integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.RequestType != ApprovalRequestTypes.AttendancePunch
            || await requests.FindByApprovalRequestIdAsync(integrationEvent.ApprovalRequestId, cancellationToken) is not { Status: PunchRequestStatus.Pending } request)
        {
            return;
        }

        var nowUtc = clock.GetUtcNow().UtcDateTime;
        var direction = request.Direction == PunchDirection.In ? PunchDirectionDto.In : PunchDirectionDto.Out;
        var recorded = await recorder.RecordAsync(
            new PunchInput(request.TenantId, request.EmployeeId, request.OccurredAtUtc, direction, PunchSourceDto.Request, request.Id.ToString(),
                null, null, null, null, false, null, request.Reason),
            cancellationToken);
        if (recorded.IsFailure)
        {
            request.Reject(nowUtc);
        }
        else
        {
            request.Approve(recorded.Value, nowUtc);
            await recalculator.RecalculateNowAsync(request.TenantId, request.WorkDate, request.WorkDate, [request.EmployeeId], cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Workflow rejected an "AttendancePunch" request -> Rejected, no punch is created.</summary>
public sealed class PunchRequestApprovalRejectedHandler(IPunchRequestRepository requests, IAttendanceUnitOfWork unitOfWork, TimeProvider clock)
    : IIntegrationEventHandler<ApprovalRejected>
{
    public async Task Handle(ApprovalRejected integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.RequestType != ApprovalRequestTypes.AttendancePunch
            || await requests.FindByApprovalRequestIdAsync(integrationEvent.ApprovalRequestId, cancellationToken) is not { } request
            || request.Reject(clock.GetUtcNow().UtcDateTime).IsFailure)
        {
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

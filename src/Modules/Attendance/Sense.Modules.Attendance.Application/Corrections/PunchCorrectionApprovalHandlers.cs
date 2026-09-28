using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Corrections;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Workflow.Contracts.Events;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Attendance.Application.Corrections;

/// <summary>
/// Workflow approved a "PunchCorrection" request (FG-004): re-checks the period lock, then writes the requested time(s) to the
/// day through the shared <see cref="AttendanceDayUpsert"/> (a time the employee did not ask for keeps the day's current value;
/// Source = Manual, like an HR correction), which recomputes worked minutes and anomalies, and publishes TimesheetWeekCompleted
/// when that closes the week. A locked month - or times that no longer fit the day - leave the day untouched and mark the
/// request ApplyFailed instead.
/// </summary>
public sealed class PunchCorrectionApprovalCompletedHandler(
    IPunchCorrectionRequestRepository corrections, IAttendanceDayRepository days, IWorkScheduleReadService schedules,
    IShiftSettingsRepository shiftSettings, IShiftPlanLockRepository locks, IMonthlyTimesheetApprovalRepository approvals,
    IIntegrationEventOutbox outbox, IAttendanceUnitOfWork unitOfWork, TimeProvider clock)
    : IIntegrationEventHandler<ApprovalCompleted>
{
    public async Task Handle(ApprovalCompleted integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.RequestType != ApprovalRequestTypes.PunchCorrection
            || await corrections.FindByIdAsync(integrationEvent.SubjectId, cancellationToken) is not { } request
            || !request.IsAwaiting(integrationEvent.ApprovalRequestId))
        {
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        if (await ApplyAsync(request, cancellationToken))
        {
            _ = request.Approve(integrationEvent.ApprovalRequestId, now);
            await TimesheetWeekEvaluator.PublishIfCompletedAsync(
                days, schedules, outbox, request.TenantId, request.EmployeeId, request.AttendanceDate, cancellationToken);
        }
        else
        {
            _ = request.MarkApplyFailed(integrationEvent.ApprovalRequestId, now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> ApplyAsync(PunchCorrectionRequest request, CancellationToken cancellationToken)
    {
        if (await PunchCorrectionPeriodLock.IsLockedAsync(locks, approvals, request.EmployeeId, request.AttendanceDate, cancellationToken))
        {
            return false;
        }

        var existing = await days.FindByEmployeeAndDateAsync(request.EmployeeId, request.AttendanceDate, cancellationToken);
        var limits = AttendanceDayUpsert.ToShiftLimits(await shiftSettings.FindAsync(cancellationToken));
        var upserted = await AttendanceDayUpsert.UpsertAsync(
            days, schedules, request.TenantId, request.EmployeeId, request.AttendanceDate,
            request.RequestedCheckIn ?? existing?.CheckIn, request.RequestedCheckOut ?? existing?.CheckOut,
            AttendanceDaySource.Manual, overnight: false, cancellationToken, existing?.ActualBreakMinutes ?? 0, limits);
        return upserted.IsSuccess;
    }
}

/// <summary>Workflow rejected a "PunchCorrection" request (FG-004) → Rejected; the day is not touched.</summary>
public sealed class PunchCorrectionApprovalRejectedHandler(IPunchCorrectionRequestRepository corrections, IAttendanceUnitOfWork unitOfWork, TimeProvider clock)
    : IIntegrationEventHandler<ApprovalRejected>
{
    public async Task Handle(ApprovalRejected integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.RequestType != ApprovalRequestTypes.PunchCorrection
            || await corrections.FindByIdAsync(integrationEvent.SubjectId, cancellationToken) is not { } request
            || request.Reject(integrationEvent.ApprovalRequestId, clock.GetUtcNow().UtcDateTime).IsFailure)
        {
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

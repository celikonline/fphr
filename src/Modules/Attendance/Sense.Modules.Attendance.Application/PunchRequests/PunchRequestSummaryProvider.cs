using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Workflow.Contracts;

namespace Sense.Modules.Attendance.Application.PunchRequests;

/// <summary>Workflow inbox/karar ekranı için "AttendancePunch" talebinin özeti: istenen okutma (zaman, yön, gerekçe).</summary>
public sealed class PunchRequestApprovalSummaryProvider(IPunchRequestRepository requests) : IApprovalRequestSummaryProvider
{
    public string RequestType => ApprovalRequestTypes.AttendancePunch;

    public async Task<ApprovalRequestSummary?> GetSummaryAsync(Guid tenantId, Guid subjectId, CancellationToken cancellationToken = default)
    {
        var found = await requests.GetByIdAsync(subjectId, cancellationToken);
        if (found.IsFailure || found.Value.TenantId != tenantId)
        {
            return null;
        }

        var request = found.Value;
        var direction = request.Direction == PunchDirection.In ? "in" : "out";
        return new ApprovalRequestSummary(
            request.WorkDate, AttendancePunch: new AttendancePunchApprovalSummary(request.OccurredAtUtc, direction, request.Reason));
    }
}

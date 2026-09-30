using Microsoft.EntityFrameworkCore;
using Sense.Modules.Attendance.Infrastructure.Persistence;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Workflow.Contracts;

namespace Sense.Modules.Attendance.Infrastructure.Lookups;

/// <summary>
/// Workflow.Contracts.IApprovalRequestSummaryProvider for RequestType "PunchCorrection" (FG-004): projects the submitted
/// PunchCorrectionRequest (SubjectId) into the day, the requested times and the day's times at submit, so the approver sees
/// what changes. AsNoTracking - a read for display.
/// </summary>
public sealed class PunchCorrectionApprovalRequestSummaryProvider(AttendanceDbContext context) : IApprovalRequestSummaryProvider
{
    public string RequestType => ApprovalRequestTypes.PunchCorrection;

    public async Task<ApprovalRequestSummary?> GetSummaryAsync(Guid tenantId, Guid subjectId, CancellationToken cancellationToken = default)
    {
        var summary = await context.PunchCorrectionRequests.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.Id == subjectId)
            .Select(r => new PunchCorrectionApprovalSummary(
                r.AttendanceDate, r.RequestedCheckIn, r.RequestedCheckOut, r.CurrentCheckIn, r.CurrentCheckOut, r.Reason))
            .FirstOrDefaultAsync(cancellationToken);
        return summary is null ? null : new ApprovalRequestSummary(summary.Date, PunchCorrection: summary);
    }
}

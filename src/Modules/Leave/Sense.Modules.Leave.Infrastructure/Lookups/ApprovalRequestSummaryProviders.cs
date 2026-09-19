using Microsoft.EntityFrameworkCore;
using Sense.Modules.Leave.Infrastructure.Persistence;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Workflow.Contracts;

namespace Sense.Modules.Leave.Infrastructure.Lookups;

/// <summary>
/// Workflow.Contracts.IApprovalRequestSummaryProvider for RequestType "Leave": projects the submitted LeaveRequest
/// (SubjectId) into the date range/type/day-count a manager needs to decide, so the Workflow inbox stops showing a
/// raw SubjectId (roadmap D-01). AsNoTracking - this is a read for display, not a unit of work.
/// </summary>
public sealed class LeaveApprovalRequestSummaryProvider(LeaveDbContext context) : IApprovalRequestSummaryProvider
{
    public string RequestType => ApprovalRequestTypes.Leave;

    public async Task<ApprovalRequestSummary?> GetSummaryAsync(Guid tenantId, Guid subjectId, CancellationToken cancellationToken = default)
    {
        var request = await context.Requests.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.Id == subjectId)
            .Select(r => new { r.StartDate, r.EndDate, r.LeaveTypeId, r.LeaveTypeCodeSnapshot, r.CalculatedDays })
            .FirstOrDefaultAsync(cancellationToken);
        if (request is null)
        {
            return null;
        }

        var leaveTypeName = await context.LeaveTypes.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.Id == request.LeaveTypeId)
            .Select(t => (string?)t.Name)
            .FirstOrDefaultAsync(cancellationToken);

        var summary = new LeaveApprovalSummary(
            request.StartDate, request.EndDate, ToCamelCase(request.LeaveTypeCodeSnapshot.ToString()), leaveTypeName, request.CalculatedDays);
        return new ApprovalRequestSummary(request.StartDate, Leave: summary);
    }

    /// <summary>
    /// "Annual" -&gt; "annual", matching the web's `leave:code.*` translation keys (leave-shared.tsx useLeaveTypeNames) -
    /// those come from LeaveRequest.leaveTypeCode, an actually-enum-typed API property the shared JsonStringEnumConverter
    /// camelCases automatically. This field crosses the Workflow.Contracts module boundary as a plain string (Workflow
    /// can't reference Leave.Domain's LeaveTypeCode enum), so ASP.NET's converter never sees it - camelCase it by hand.
    /// </summary>
    private static string ToCamelCase(string value) => value.Length == 0 ? value : char.ToLowerInvariant(value[0]) + value[1..];
}

/// <summary>
/// Workflow.Contracts.IApprovalRequestSummaryProvider for RequestType "Overtime": projects the submitted
/// OvertimeRequest (SubjectId) into the work date/start time/duration/reason a manager needs to decide (roadmap D-01).
/// </summary>
public sealed class OvertimeApprovalRequestSummaryProvider(LeaveDbContext context) : IApprovalRequestSummaryProvider
{
    public string RequestType => ApprovalRequestTypes.Overtime;

    public async Task<ApprovalRequestSummary?> GetSummaryAsync(Guid tenantId, Guid subjectId, CancellationToken cancellationToken = default)
    {
        var request = await context.OvertimeRequests.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.Id == subjectId)
            .Select(r => new { r.WorkDate, r.StartTime, r.DurationMinutes, r.Description })
            .FirstOrDefaultAsync(cancellationToken);
        if (request is null)
        {
            return null;
        }

        var summary = new OvertimeApprovalSummary(request.WorkDate, request.StartTime, request.DurationMinutes, request.Description);
        return new ApprovalRequestSummary(request.WorkDate, Overtime: summary);
    }
}

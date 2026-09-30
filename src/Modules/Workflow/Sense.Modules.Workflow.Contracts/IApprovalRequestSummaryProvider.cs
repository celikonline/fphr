namespace Sense.Modules.Workflow.Contracts;

/// <summary>Fazla mesai ("Overtime") request's projection for the inbox/decision screens.</summary>
public sealed record OvertimeApprovalSummary(DateOnly WorkDate, TimeOnly StartTime, int DurationMinutes, string? Reason);

/// <summary>
/// İzin ("Leave") request's projection for the inbox/decision screens. LeaveTypeCode is the LeaveTypeCode enum name,
/// camelCased by the provider (e.g. "annual") to match the web's `leave:code.*` translation keys - it crosses this
/// module boundary as a plain string, so the shared JsonStringEnumConverter's camelCase policy never sees it.
/// </summary>
public sealed record LeaveApprovalSummary(
    DateOnly StartDate, DateOnly EndDate, string LeaveTypeCode, string? LeaveTypeName, decimal Days, string? Description = null);

/// <summary>Missing-punch ("AttendancePunch") request's projection: the punch being asked for. Direction is the camelCase PunchDirection name ("in"/"out"/"unknown").</summary>
public sealed record AttendancePunchApprovalSummary(DateTime OccurredAtUtc, string Direction, string? Reason);

/// <summary>Payslip objection ("PayslipObjection") projection: the payroll period label (e.g. "2026-08") and the employee's stated reason.</summary>
public sealed record PayslipObjectionApprovalSummary(string Period, string? Reason);

/// <summary>
/// Request-type-specific projection of an <c>ApprovalRequest</c>'s subject entity (a future LeaveRequest/OvertimeRequest/...),
/// so the Workflow inbox/decision screens show more than the opaque SubjectId - the requester picked a date, a duration
/// or a range, and the approver needs that to decide, not a GUID. RequestDate is the request's own date (the overtime
/// work date, or the leave start date), shown next to the ApprovalRequest's SubmittedAt. Exactly one of
/// <see cref="Overtime"/>/<see cref="Leave"/>/<see cref="AttendancePunch"/>/<see cref="PayslipObjection"/>/<see cref="PunchCorrection"/> is set today, matching RequestType; both null is not constructible -
/// a provider that finds nothing returns a null <see cref="ApprovalRequestSummary"/> instead (see
/// <see cref="IApprovalRequestSummaryProvider.GetSummaryAsync"/>).
/// </summary>
public sealed record ApprovalRequestSummary(
    DateOnly RequestDate, OvertimeApprovalSummary? Overtime = null, LeaveApprovalSummary? Leave = null,
    AttendancePunchApprovalSummary? AttendancePunch = null, PayslipObjectionApprovalSummary? PayslipObjection = null,
    PunchCorrectionApprovalSummary? PunchCorrection = null);

/// <summary>
/// Eksik giriş/çıkış düzeltmesi ("PunchCorrection", FG-004) projection: the day, the times the employee asks for, and the
/// day's check-in/out as they were when the request was submitted (null = no punch recorded then).
/// </summary>
public sealed record PunchCorrectionApprovalSummary(
    DateOnly Date, TimeOnly? RequestedCheckIn, TimeOnly? RequestedCheckOut, TimeOnly? CurrentCheckIn, TimeOnly? CurrentCheckOut, string Reason);

/// <summary>
/// A producing module's read port for <see cref="ApprovalRequestSummary"/>, one implementation per RequestType
/// (Organization.Contracts.ApprovalRequestTypes) - e.g. Leave.Infrastructure registers one for "Leave" and one for
/// "Overtime". Workflow.Application resolves the provider whose <see cref="RequestType"/> matches an ApprovalRequest
/// and asks it to summarize <c>ApprovalRequest.SubjectId</c> - Workflow itself never dereferences that id (still
/// opaque to Workflow, per ApprovalRequest.SubjectId's own doc comment). A RequestType with no registered provider
/// (Advance/Expense/Requisition today) simply gets no summary - the inbox falls back to the request type label alone.
/// </summary>
public interface IApprovalRequestSummaryProvider
{
    /// <summary>One of Organization.Contracts.ApprovalRequestTypes (e.g. "Leave", "Overtime").</summary>
    string RequestType { get; }

    /// <summary>Null when <paramref name="subjectId"/> no longer resolves (deleted/unreachable) - the caller then shows no summary, not an error.</summary>
    Task<ApprovalRequestSummary?> GetSummaryAsync(Guid tenantId, Guid subjectId, CancellationToken cancellationToken = default);
}

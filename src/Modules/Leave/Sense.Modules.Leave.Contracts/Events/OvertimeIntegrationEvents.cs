using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Leave.Contracts.Events;

/// <summary>Published (via Leave's outbox) when an overtime request is submitted by the employee or on their behalf (Source "Manual"/"Admin").</summary>
public sealed record OvertimeRequestSubmitted(
    Guid TenantId, Guid OvertimeRequestId, Guid EmployeeId, DateOnly WorkDate, TimeOnly StartTime, int DurationMinutes, int PayableMinutes, string Source)
    : IntegrationEvent(TenantId);

/// <summary>Published when Workflow approved an overtime request (Attendance/Payroll consume approved minutes later).</summary>
public sealed record OvertimeApproved(Guid TenantId, Guid OvertimeRequestId, Guid EmployeeId, DateOnly WorkDate, int DurationMinutes, int PayableMinutes)
    : IntegrationEvent(TenantId);

/// <summary>Published when Workflow rejected an overtime request.</summary>
public sealed record OvertimeRejected(Guid TenantId, Guid OvertimeRequestId, Guid EmployeeId) : IntegrationEvent(TenantId);

/// <summary>Published when approved overtime was converted to extra leave (an ExtraGrant ledger row).</summary>
public sealed record OvertimeConvertedToTimeOff(
    Guid TenantId, Guid OvertimeRequestId, Guid ConversionId, Guid EmployeeId, Guid LeaveTypeId, string MinutesBasis, int ResultMinutes, decimal ResultDays)
    : IntegrationEvent(TenantId);

/// <summary>Published when a written overtime consent is recorded (11.3.9 "Leave.OvertimeConsentRecorded").</summary>
public sealed record OvertimeConsentRecorded(Guid TenantId, Guid ConsentId, Guid EmployeeId, DateOnly GivenAt, bool CoversNightWork)
    : IntegrationEvent(TenantId);

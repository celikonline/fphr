using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Attendance.Domain.AttendanceDays;

/// <summary>
/// Audit trail of how a flagged <see cref="AttendanceDay"/> was addressed (11.3.6 "Çözüm etkisi"): one row per
/// resolve action. <see cref="LinkedEntityType"/>/<see cref="LinkedEntityId"/> point at the OvertimeRequest or
/// LeaveRequest created in Leave (via <c>Leave.Contracts.ILeaveCommandService</c>) for AddOvertime/CreateLeaveRequest;
/// both are null for SetWorkedHours/Ignore, which only touch this day.
/// </summary>
public sealed class AnomalyResolution : TenantAggregateRoot<Guid>
{
    private AnomalyResolution()
    {
    }

    private AnomalyResolution(
        Guid id, Guid tenantId, Guid attendanceDayId, AttendanceAnomaly anomaly, AnomalyResolutionAction action,
        string? linkedEntityType, Guid? linkedEntityId, Guid? resolvedByUserId, DateTime resolvedAt)
        : base(id, tenantId)
    {
        AttendanceDayId = attendanceDayId;
        Anomaly = anomaly;
        Action = action;
        LinkedEntityType = linkedEntityType;
        LinkedEntityId = linkedEntityId;
        ResolvedByUserId = resolvedByUserId;
        ResolvedAt = resolvedAt;
    }

    public Guid AttendanceDayId { get; private set; }

    /// <summary>Snapshot of the day's flags at the moment of resolution (the day's own flags never change once computed).</summary>
    public AttendanceAnomaly Anomaly { get; private set; }

    public AnomalyResolutionAction Action { get; private set; }

    public string? LinkedEntityType { get; private set; }

    public Guid? LinkedEntityId { get; private set; }

    public Guid? ResolvedByUserId { get; private set; }

    public DateTime ResolvedAt { get; private set; }

    public static AnomalyResolution Create(
        Guid tenantId, Guid attendanceDayId, AttendanceAnomaly anomaly, AnomalyResolutionAction action,
        string? linkedEntityType, Guid? linkedEntityId, Guid? resolvedByUserId, DateTime resolvedAt) =>
        new(Guid.CreateVersion7(), tenantId, attendanceDayId, anomaly, action, linkedEntityType, linkedEntityId, resolvedByUserId, resolvedAt);
}

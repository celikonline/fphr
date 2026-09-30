namespace Sense.Modules.Payroll.Domain.Payslips;

/// <summary>
/// K4 evidence of an earlier payslip version, kept when a recalculation resets the live acknowledgement/objection
/// fields (<see cref="Payslip.AcknowledgementHistory"/>, append-only). <see cref="Version"/> is the version the employee
/// acknowledged or objected to; objection fields are null when that version had no decided objection.
/// </summary>
public sealed record PayslipAcknowledgementRecord(
    int Version,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? AcknowledgedAt,
    string? Ip,
    string? UserAgent,
    PayslipObjectionStatus? ObjectionStatus,
    string? ObjectionReason,
    DateTimeOffset? ObjectedAt,
    Guid? ObjectionApprovalRequestId,
    DateTimeOffset? ObjectionResolvedAt);

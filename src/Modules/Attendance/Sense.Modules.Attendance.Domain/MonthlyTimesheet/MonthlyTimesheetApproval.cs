using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Attendance.Domain.MonthlyTimesheet;

/// <summary>
/// Records that one employee's monthly puantaj row ("Aylık Puantaj" ekranı, S-11) has been approved for a calendar
/// month: one row per (tenant, EmployeeId, <see cref="PeriodMonth"/>). No row = not approved yet. Unlike
/// <c>ShiftPlanLock</c> this does not block edits to the underlying AttendanceDay/leave data - re-approving after a
/// correction is expected (HR re-runs "Tümünü Onayla" for the month), so this is a simple flag with history, not a
/// lock.
/// </summary>
public sealed class MonthlyTimesheetApproval : TenantAggregateRoot<Guid>
{
    private MonthlyTimesheetApproval()
    {
    }

    private MonthlyTimesheetApproval(Guid id, Guid tenantId, Guid employeeId, DateOnly periodMonth, DateTime approvedAt, Guid? approvedByUserId)
        : base(id, tenantId)
    {
        EmployeeId = employeeId;
        PeriodMonth = periodMonth;
        ApprovedAt = approvedAt;
        ApprovedByUserId = approvedByUserId;
    }

    public Guid EmployeeId { get; private set; }

    /// <summary>Normalized to the first day of the month.</summary>
    public DateOnly PeriodMonth { get; private set; }

    public DateTime ApprovedAt { get; private set; }

    public Guid? ApprovedByUserId { get; private set; }

    public static MonthlyTimesheetApproval Approve(Guid tenantId, Guid employeeId, DateOnly periodMonth, DateTime approvedAt, Guid? approvedByUserId) =>
        new(Guid.CreateVersion7(), tenantId, employeeId, NormalizeToMonthStart(periodMonth), approvedAt, approvedByUserId);

    /// <summary>Re-approve (e.g. after "Tüm Veriyi Güncelle"): bumps the timestamp/actor, same row.</summary>
    public void Reapprove(DateTime approvedAt, Guid? approvedByUserId)
    {
        ApprovedAt = approvedAt;
        ApprovedByUserId = approvedByUserId;
    }

    public static DateOnly NormalizeToMonthStart(DateOnly date) => new(date.Year, date.Month, 1);
}

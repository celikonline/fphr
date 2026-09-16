using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Domain.Compensatory;

public enum CompensatoryWorkStatus
{
    Open = 0,
    Completed = 1,
    Expired = 2,
}

/// <summary>
/// Telafi çalışması borcu (11.3.6 [Mevzuat]): one row per (employee, ISO week) whose <see cref="Domain.AttendanceDays.AttendanceDay.PlannedMinutes"/>
/// total fell short of the standard 45s/week norm (a deliberate "kısa çalışma" schedule reduction, not an attendance
/// shortfall - see <see cref="CompensatoryWorkLimits"/>). Posted by TimesheetWeekCompletedHandler once a week's
/// puantaj is fully resolved (package A5 MVP: flat 45s/week reference and a fixed 2-month window, not the
/// tenant-configurable <c>LegislationParameter</c> the spec envisions - see faz2-durum-takibi.md).
/// </summary>
public sealed class CompensatoryWorkLedger : TenantAggregateRoot<Guid>
{
    private CompensatoryWorkLedger()
    {
    }

    private CompensatoryWorkLedger(Guid id, Guid tenantId, Guid employeeId, DateOnly weekStart, int deficitMinutes, DateOnly dueDate)
        : base(id, tenantId)
    {
        EmployeeId = employeeId;
        WeekStart = weekStart;
        DeficitMinutes = deficitMinutes;
        DueDate = dueDate;
        Status = CompensatoryWorkStatus.Open;
    }

    public Guid EmployeeId { get; private set; }

    public DateOnly WeekStart { get; private set; }

    public int DeficitMinutes { get; private set; }

    public int CompensatedMinutes { get; private set; }

    /// <summary>md.64: telafi süresi (varsayılan 2 ay, pandemi döneminde 4 aya çıkarılmıştı).</summary>
    public DateOnly DueDate { get; private set; }

    public CompensatoryWorkStatus Status { get; private set; }

    public static CompensatoryWorkLedger Open(Guid tenantId, Guid employeeId, DateOnly weekStart, int deficitMinutes, DateOnly dueDate) =>
        new(Guid.CreateVersion7(), tenantId, employeeId, weekStart, deficitMinutes, dueDate);

    /// <summary>Week re-resolved (e.g. a corrected import) after this row was posted: the deficit is recomputed while still Open.</summary>
    public Result AdjustDeficit(int deficitMinutes)
    {
        if (Status != CompensatoryWorkStatus.Open)
        {
            return Error.Conflict(AttendanceDomainErrors.CompensatoryLedgerNotOpen);
        }

        DeficitMinutes = deficitMinutes;
        if (CompensatedMinutes >= DeficitMinutes)
        {
            Status = CompensatoryWorkStatus.Completed;
        }

        return Result.Success();
    }

    /// <summary>Manager/HR logs telafi hours actually worked; caps at the outstanding deficit.</summary>
    public Result RecordCompensation(int minutes)
    {
        if (Status != CompensatoryWorkStatus.Open)
        {
            return Error.Conflict(AttendanceDomainErrors.CompensatoryLedgerNotOpen);
        }

        if (minutes <= 0)
        {
            return Error.Validation(AttendanceDomainErrors.CompensatoryMinutesInvalid);
        }

        CompensatedMinutes = Math.Min(CompensatedMinutes + minutes, DeficitMinutes);
        if (CompensatedMinutes >= DeficitMinutes)
        {
            Status = CompensatoryWorkStatus.Completed;
        }

        return Result.Success();
    }

    public void ExpireIfPastDue(DateOnly today)
    {
        if (Status == CompensatoryWorkStatus.Open && today > DueDate)
        {
            Status = CompensatoryWorkStatus.Expired;
        }
    }
}

/// <summary>MVP constants standing in for the spec's tenant-configurable LegislationParameter values.</summary>
public static class CompensatoryWorkLimits
{
    public const int StandardWeeklyMinutes = 45 * 60;
    public const int WindowMonths = 2;
}

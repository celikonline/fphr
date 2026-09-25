using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Domain.Shifts;

public enum ShiftAssignmentStatus
{
    Draft = 0,
    Published = 1,
}

/// <summary>
/// One employee's one calendar day of planned shift (11.3.6 "Vardiya kaydı"): haftalık görünümde girilir, Not/Tekrarla
/// destekler, Yayınla ile çalışana bildirilir. One row per (EmployeeId, Date) - reassigning a day overwrites it like
/// <see cref="AttendanceDays.AttendanceDay"/>'s re-record semantics, as long as its period is not locked (the
/// application layer checks <see cref="ShiftPlanLock"/> before calling any mutator here).
/// </summary>
public sealed class ShiftAssignment : TenantAggregateRoot<Guid>
{
    private ShiftAssignment()
    {
    }

    private ShiftAssignment(Guid id, Guid tenantId, Guid employeeId, DateOnly date, TimeOnly start, TimeOnly end, int breakMinutes, bool isNight, string? note)
        : base(id, tenantId)
    {
        EmployeeId = employeeId;
        Date = date;
        Status = ShiftAssignmentStatus.Draft;
        Update(start, end, breakMinutes, isNight, note);
    }

    public Guid EmployeeId { get; private set; }

    public DateOnly Date { get; private set; }

    public TimeOnly Start { get; private set; }

    public TimeOnly End { get; private set; }

    public int BreakMinutes { get; private set; }

    public bool IsNight { get; private set; }

    public string? Note { get; private set; }

    public ShiftAssignmentStatus Status { get; private set; }

    public DateTime? PublishedAt { get; private set; }

    /// <summary>Kural setli vardiya tanımı (Z0.2); null = yalnız bu satırın saatleri (geriye uyumlu eski atamalar).</summary>
    public Guid? ShiftDefinitionId { get; private set; }

    /// <summary>Günün türü; <see cref="ShiftDayType.Work"/> dışında saat alanları hesaplamada yok sayılır. Eski satırlar Work.</summary>
    public ShiftDayType DayType { get; private set; }

    /// <summary>Total planned minutes, break excluded; handles an overnight shift (End &lt;= Start means it crosses midnight).</summary>
    public int ShiftMinutes
    {
        get
        {
            var span = End.ToTimeSpan() - Start.ToTimeSpan();
            if (span <= TimeSpan.Zero)
            {
                span += TimeSpan.FromDays(1);
            }

            return Math.Max((int)span.TotalMinutes - BreakMinutes, 0);
        }
    }

    public static Result<ShiftAssignment> Create(Guid tenantId, Guid employeeId, DateOnly date, TimeOnly start, TimeOnly end, int breakMinutes, bool isNight, string? note)
    {
        var check = ValidateTimes(start, end, breakMinutes);
        if (check.IsFailure)
        {
            return check.Error;
        }

        return new ShiftAssignment(Guid.CreateVersion7(), tenantId, employeeId, date, start, end, breakMinutes, isNight, note);
    }

    /// <summary>"Not ekle" + saat düzenleme; kilit tarihine kadar serbest (uygulama katmanı kilidi kontrol eder).</summary>
    public Result Update(TimeOnly start, TimeOnly end, int breakMinutes, bool isNight, string? note)
    {
        var check = ValidateTimes(start, end, breakMinutes);
        if (check.IsFailure)
        {
            return check;
        }

        Start = start;
        End = end;
        BreakMinutes = breakMinutes;
        IsNight = isNight;
        Note = note;
        return Result.Success();
    }

    /// <summary>"Yayınla": çalışana bildirilir (Notifications şablonu uygulama katmanında tetiklenir).</summary>
    public Result Publish(DateTime publishedAt)
    {
        if (Status == ShiftAssignmentStatus.Published)
        {
            return Error.Conflict(AttendanceDomainErrors.ShiftAlreadyPublished);
        }

        Status = ShiftAssignmentStatus.Published;
        PublishedAt = publishedAt;
        return Result.Success();
    }

    /// <summary>Günü bir <see cref="ShiftDefinition"/>'a bağlar ve gün türünü belirler (kilit kontrolü uygulama katmanında).</summary>
    public Result SetPlan(Guid? shiftDefinitionId, ShiftDayType dayType)
    {
        if (!Enum.IsDefined(dayType))
        {
            return Error.Validation(AttendanceDomainErrors.ShiftDayTypeInvalid);
        }

        ShiftDefinitionId = shiftDefinitionId;
        DayType = dayType;
        return Result.Success();
    }

    private static Result ValidateTimes(TimeOnly start, TimeOnly end, int breakMinutes)
    {
        if (start == end || breakMinutes < 0)
        {
            return Error.Validation(AttendanceDomainErrors.ShiftTimeRangeInvalid);
        }

        return Result.Success();
    }
}

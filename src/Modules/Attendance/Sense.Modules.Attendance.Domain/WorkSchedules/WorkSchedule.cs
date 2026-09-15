using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Domain.WorkSchedules;

/// <summary>
/// Çalışma takvimi (11.3.6): a day-based table, not a rule set — "Ayarlar &gt; Kurallar &gt; Çalışma takvimleri".
/// One default per tenant ("Genel çalışma tablosu", seeded lazily like Leave's default rule set). Employees are
/// linked through dated <see cref="EmployeeWorkScheduleAssignment"/> rows, never a direct FK, so re-assigning an
/// employee never rewrites history.
/// </summary>
public sealed class WorkSchedule : TenantAggregateRoot<Guid>
{
    private List<WorkScheduleDay> _days = [];

    private WorkSchedule()
    {
    }

    private WorkSchedule(Guid id, Guid tenantId, string name, IReadOnlyList<WorkScheduleDay> days, bool includeSaturdayWhenFullWeek, bool isDefault)
        : base(id, tenantId)
    {
        Name = name;
        _days = days.ToList();
        IncludeSaturdayWhenFullWeek = includeSaturdayWhenFullWeek;
        IsDefault = isDefault;
        IsActive = true;
    }

    public string Name { get; private set; } = string.Empty;

    public bool IsDefault { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>"Pazartesi'den Cuma'ya izin alırsa Cumartesi'yi ekle" — consumed by Leave's day counter.</summary>
    public bool IncludeSaturdayWhenFullWeek { get; private set; }

    public IReadOnlyList<WorkScheduleDay> Days => _days.AsReadOnly();

    public int WeeklyMinutes => _days.Sum(d => d.WorkingMinutes());

    public WorkScheduleDay GetDay(DayOfWeek dayOfWeek) =>
        _days.First(d => d.DayOfWeek == dayOfWeek);

    public static Result<WorkSchedule> Create(
        Guid tenantId, string name, IReadOnlyList<WorkScheduleDay> days, bool includeSaturdayWhenFullWeek = false, bool isDefault = false)
    {
        var nameCheck = ValidateName(name);
        if (nameCheck.IsFailure)
        {
            return nameCheck.Error;
        }

        var daysCheck = ValidateDays(days);
        if (daysCheck.IsFailure)
        {
            return daysCheck.Error;
        }

        return new WorkSchedule(Guid.CreateVersion7(), tenantId, name.Trim(), days, includeSaturdayWhenFullWeek, isDefault);
    }

    /// <summary>Seed: "Genel çalışma tablosu", Mon–Fri 09:00–18:00 (break 12:30–13:30), Sat/Sun off.</summary>
    public static WorkSchedule CreateDefault(Guid tenantId, string name)
    {
        var start = new TimeOnly(9, 0);
        var end = new TimeOnly(18, 0);
        var breakStart = new TimeOnly(12, 30);
        var breakEnd = new TimeOnly(13, 30);
        var days = Enum.GetValues<DayOfWeek>()
            .Select(day => day is DayOfWeek.Saturday or DayOfWeek.Sunday
                ? WorkScheduleDay.Off(day)
                : WorkScheduleDay.Working(day, start, end, breakStart, breakEnd))
            .ToList();

        return new WorkSchedule(Guid.CreateVersion7(), tenantId, name, days, includeSaturdayWhenFullWeek: true, isDefault: true);
    }

    public Result Rename(string name)
    {
        var check = ValidateName(name);
        if (check.IsFailure)
        {
            return check;
        }

        Name = name.Trim();
        return Result.Success();
    }

    public Result SetDays(IReadOnlyList<WorkScheduleDay> days, bool includeSaturdayWhenFullWeek)
    {
        var check = ValidateDays(days);
        if (check.IsFailure)
        {
            return check;
        }

        _days = days.ToList();
        IncludeSaturdayWhenFullWeek = includeSaturdayWhenFullWeek;
        return Result.Success();
    }

    /// <summary>"Varsayılan yap": exactly one default schedule per tenant afterwards (new employees get it).</summary>
    public void MakeDefault(IEnumerable<WorkSchedule> tenantSchedules)
    {
        foreach (var other in tenantSchedules.Where(s => s.Id != Id))
        {
            other.IsDefault = false;
        }

        IsDefault = true;
    }

    public void SetActive(bool isActive) => IsActive = isActive;

    /// <summary>Delete guard: the default schedule and schedules employees are still assigned to cannot be deleted.</summary>
    public Result EnsureDeletable(int assignedEmployeeCount)
    {
        if (IsDefault)
        {
            return Error.Conflict(AttendanceDomainErrors.ScheduleIsDefault);
        }

        return assignedEmployeeCount > 0 ? Error.Conflict(AttendanceDomainErrors.ScheduleHasEmployees) : Result.Success();
    }

    private static Result ValidateName(string name) =>
        string.IsNullOrWhiteSpace(name) || name.Length > AttendanceLimits.NameMaxLength
            ? Error.Validation(AttendanceDomainErrors.ScheduleNameRequired)
            : Result.Success();

    private static Result ValidateDays(IReadOnlyList<WorkScheduleDay> days)
    {
        if (days.Count != AttendanceLimits.DaysPerWeek || days.Select(d => d.DayOfWeek).Distinct().Count() != AttendanceLimits.DaysPerWeek)
        {
            return Error.Validation(AttendanceDomainErrors.ScheduleDaysCountInvalid);
        }

        foreach (var day in days)
        {
            var check = day.Validate();
            if (check.IsFailure)
            {
                return check;
            }
        }

        return Result.Success();
    }
}

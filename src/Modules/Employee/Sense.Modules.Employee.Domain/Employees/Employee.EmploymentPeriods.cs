using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Employee.Domain.Employees;

/// <summary>What a rehire ("Yeniden işe al", 11.3.03 E5a) produced: the new open period and the dates it suggests.</summary>
public sealed record RehireOutcome(EmploymentPeriod Period, DateOnly LeaveAccrualStartDate, DateOnly SeniorityStartDate);

/// <summary>
/// Employment periods and the seniority they add up to ("Çalışma dönemleri ve kıdem birleştirme", 11.3.03 E5a).
/// Like salary/bank records this collection is not auto-included: only the termination/rehire/seniority use cases load
/// it, so every method here assumes it was loaded (an employee loaded without it looks like one with no periods, and
/// the seniority methods then fall back to the hire date).
/// </summary>
public sealed partial class Employee
{
    private readonly List<EmploymentPeriod> _employmentPeriods = [];

    /// <summary>Employment periods (EF navigation; use <see cref="EmploymentTimeline"/> for the ordered view).</summary>
    public IReadOnlyCollection<EmploymentPeriod> EmploymentPeriods => _employmentPeriods.AsReadOnly();

    /// <summary>Periods ordered by start date (oldest first); the last one is open while the employee works.</summary>
    public IReadOnlyList<EmploymentPeriod> EmploymentTimeline => _employmentPeriods.OrderBy(p => p.StartDate).ThenBy(p => p.Id).ToList();

    public EmploymentPeriod? OpenEmploymentPeriod => _employmentPeriods.FirstOrDefault(p => p.IsOpen);

    /// <summary>
    /// Writes the first period from the hire date for an employee that has none yet (created before employment periods
    /// existed, or hired through any path that predates them); a terminated employee gets it closed on its termination
    /// date. Returns null when nothing was created - no hire date, or periods already exist.
    /// </summary>
    public EmploymentPeriod? EnsureInitialEmploymentPeriod()
    {
        if (_employmentPeriods.Count > 0 || HireDate is not { } hireDate)
        {
            return null;
        }

        var period = new EmploymentPeriod(Guid.CreateVersion7(), TenantId, Id, hireDate, EmploymentPeriodSource.Hire);
        if (Status == EmployeeStatus.Terminated && TerminationDate is { } terminationDate)
        {
            period.Close(terminationDate, TerminationReasonId, severancePaid: false, leaveSettledAtExit: false);
        }

        _employmentPeriods.Add(period);
        return period;
    }

    /// <summary>
    /// Re-hires a terminated employee: opens a new period, clears the termination fields and moves the status back to
    /// Active. <paramref name="mergePreviousPeriods"/> (the wizard's "önceki dönem izin kıdemine sayılsın mı" checkbox)
    /// decides whether the counted previous periods move <see cref="LeaveAccrualStartDate"/> back; an explicit
    /// <paramref name="leaveAccrualStartDate"/> always wins (HR override, 11.3.03).
    /// </summary>
    public Result<RehireOutcome> Rehire(DateOnly hireDate, bool mergePreviousPeriods, DateOnly? leaveAccrualStartDate = null)
    {
        if (Status != EmployeeStatus.Terminated)
        {
            return Error.Rule(EmployeeDomainErrors.RehireInvalidStatus);
        }

        EnsureInitialEmploymentPeriod();

        var lastEnd = _employmentPeriods.Max(p => p.EndDate) ?? TerminationDate;
        if (lastEnd is { } previousEnd && hireDate <= previousEnd)
        {
            return Error.Rule(EmployeeDomainErrors.RehireDateBeforePreviousExit);
        }

        var suggestedLeaveStart = SuggestedStart(SeniorityPurpose.Leave, hireDate);
        var suggestedSeniorityStart = SuggestedStart(SeniorityPurpose.Severance, hireDate);

        var period = new EmploymentPeriod(Guid.CreateVersion7(), TenantId, Id, hireDate, EmploymentPeriodSource.Rehire);
        _employmentPeriods.Add(period);

        HireDate = hireDate;
        LeaveAccrualStartDate = leaveAccrualStartDate ?? (mergePreviousPeriods ? suggestedLeaveStart : hireDate);
        TerminationDate = null;
        TerminationReasonId = null;
        TerminationType = null;
        SgkExitCode = null;
        Status = EmployeeStatus.Active;
        Raise(new EmployeeStatusChanged(Id, TenantId, Status));

        return new RehireOutcome(period, LeaveAccrualStartDate.Value, mergePreviousPeriods ? suggestedSeniorityStart : hireDate);
    }

    /// <summary>Closes the open period on termination; a no-op when no period is loaded/open (see the class remarks).</summary>
    internal void CloseOpenEmploymentPeriod(DateOnly endDate, Guid? terminationReasonId, bool severancePaid, bool leaveSettledAtExit) =>
        OpenEmploymentPeriod?.Close(endDate, terminationReasonId, severancePaid, leaveSettledAtExit);

    /// <summary>Days that count towards <paramref name="purpose"/> across all periods, both ends inclusive; open periods count up to <paramref name="asOf"/>.</summary>
    public int CountedSeniorityDays(SeniorityPurpose purpose, DateOnly asOf)
    {
        if (_employmentPeriods.Count > 0)
        {
            return _employmentPeriods.Where(p => p.CountsFor(purpose)).Sum(p => p.CountedDays(asOf));
        }

        return ImplicitSeniorityStart(purpose) is { } start && asOf >= start ? asOf.DayNumber - start.DayNumber + 1 : 0;
    }

    /// <summary>
    /// The date the counted seniority for <paramref name="purpose"/> effectively starts: <paramref name="asOf"/> minus
    /// the counted days, so gaps between periods and uncounted periods are deducted. Falls back to the hire date (or an
    /// earlier <see cref="LeaveAccrualStartDate"/>) when no periods are recorded; null when there is no hire date.
    /// </summary>
    public DateOnly? SeniorityStart(SeniorityPurpose purpose, DateOnly asOf)
    {
        if (_employmentPeriods.Count == 0)
        {
            return ImplicitSeniorityStart(purpose);
        }

        // Counted days include both ends, so the equivalent continuous start is asOf - (days - 1).
        var days = CountedSeniorityDays(purpose, asOf);
        return days == 0 ? null : asOf.AddDays(1 - days);
    }

    /// <summary>Seniority start of an employee with no recorded periods: the hire date, or the earlier leave accrual start when leave seniority was already carried back (group transfer, import).</summary>
    private DateOnly? ImplicitSeniorityStart(SeniorityPurpose purpose)
    {
        if (HireDate is not { } hireDate)
        {
            return null;
        }

        return purpose == SeniorityPurpose.Severance || LeaveAccrualStartDate is not { } accrualStart || accrualStart > hireDate
            ? hireDate
            : accrualStart;
    }

    /// <summary>Completed months of counted seniority for <paramref name="purpose"/> as of <paramref name="asOf"/>; 0 when there is no hire date yet.</summary>
    public int SeniorityMonths(SeniorityPurpose purpose, DateOnly asOf)
    {
        if (SeniorityStart(purpose, asOf) is not { } start || asOf < start)
        {
            return 0;
        }

        var months = ((asOf.Year - start.Year) * 12) + asOf.Month - start.Month;
        if (start.AddMonths(months) > asOf)
        {
            months--;
        }

        return Math.Max(0, months);
    }

    /// <summary>The start date a new period beginning on <paramref name="newStart"/> inherits once the counted earlier periods are carried over.</summary>
    private DateOnly SuggestedStart(SeniorityPurpose purpose, DateOnly newStart) =>
        newStart.AddDays(-_employmentPeriods.Where(p => p.CountsFor(purpose)).Sum(p => p.CountedDays(newStart)));
}

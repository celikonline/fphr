using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Modules.Employee.Application.EmploymentPeriods;

/// <summary>
/// One employment period ("Çalışma dönemi", 11.3.03 E5a). An employee hired before periods existed has none stored:
/// the read side then shows a single implicit period built from the hire date, whose id is the employee id (same
/// convention as the implicit initial career record).
/// </summary>
public sealed record EmploymentPeriodDto(
    Guid Id,
    DateOnly StartDate,
    DateOnly? EndDate,
    EmploymentPeriodSource Source,
    Guid? TerminationReasonId,
    string? TerminationReasonName,
    bool SeverancePaid,
    bool LeaveSettledAtExit,
    int CountedDays);

/// <summary>
/// Merged seniority of an employee ("Kıdem birleştirme"). Leave seniority skips periods settled at exit, severance
/// seniority skips periods whose severance was paid, and the job-security figure counts every period. The start dates
/// are "as if worked continuously" dates (as-of minus the counted days), i.e. what LeaveAccrualStartDate is suggested from.
/// </summary>
public sealed record EmploymentSeniorityDto(
    DateOnly AsOf,
    DateOnly? LeaveSeniorityStartDate,
    int LeaveSeniorityDays,
    DateOnly? SeveranceSeniorityStartDate,
    int SeveranceSeniorityDays,
    int JobSecuritySeniorityMonths);

/// <summary>Employment periods of an employee plus the seniority they add up to (GET /employees/{id}/employment-periods).</summary>
public sealed record EmploymentHistoryDto(IReadOnlyList<EmploymentPeriodDto> Periods, EmploymentSeniorityDto Seniority);

/// <summary>Shapes an employee's employment periods and seniority; shared by the query and the rehire response.</summary>
public sealed class EmploymentHistoryReader(ITerminationReasonRepository reasons, TimeProvider clock)
{
    public DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    public async Task<EmploymentHistoryDto> ReadAsync(EmployeeEntity employee, CancellationToken ct)
    {
        // Seniority of a terminated employee stops at the exit date; an active one accrues up to today.
        var asOf = employee.Status == EmployeeStatus.Terminated && employee.TerminationDate is { } exit ? exit : Today;
        var stored = employee.EmploymentTimeline;

        var reasonIds = stored.Count > 0
            ? stored.Where(p => p.TerminationReasonId is not null).Select(p => p.TerminationReasonId!.Value).ToHashSet()
            : new HashSet<Guid>(employee.TerminationReasonId is { } own ? [own] : []);

        Dictionary<Guid, string> reasonNames = [];
        if (reasonIds.Count > 0)
        {
            var catalog = await reasons.ListAsync(includeInactive: true, ct).ConfigureAwait(false);
            reasonNames = catalog.Where(r => reasonIds.Contains(r.Id)).ToDictionary(r => r.Id, r => r.Name);
        }

        var periods = stored.Count > 0
            ? stored.Select(p => new EmploymentPeriodDto(
                p.Id,
                p.StartDate,
                p.EndDate,
                p.Source,
                p.TerminationReasonId,
                p.TerminationReasonId is { } id ? reasonNames.GetValueOrDefault(id) : null,
                p.SeverancePaid,
                p.LeaveSettledAtExit,
                p.CountedDays(asOf))).ToList()
            : Implicit(employee, asOf, reasonNames);

        return new EmploymentHistoryDto(periods, Seniority(employee, asOf));
    }

    public EmploymentSeniorityDto Seniority(EmployeeEntity employee, DateOnly asOf) =>
        new(
            asOf,
            employee.SeniorityStart(SeniorityPurpose.Leave, asOf),
            employee.CountedSeniorityDays(SeniorityPurpose.Leave, asOf),
            employee.SeniorityStart(SeniorityPurpose.Severance, asOf),
            employee.CountedSeniorityDays(SeniorityPurpose.Severance, asOf),
            employee.SeniorityMonths(SeniorityPurpose.JobSecurity, asOf));

    private static List<EmploymentPeriodDto> Implicit(EmployeeEntity employee, DateOnly asOf, Dictionary<Guid, string> reasonNames)
    {
        if (employee.HireDate is not { } hireDate)
        {
            return [];
        }

        var end = employee.Status == EmployeeStatus.Terminated ? employee.TerminationDate : null;
        var days = asOf < hireDate ? 0 : asOf.DayNumber - hireDate.DayNumber + 1;
        return
        [
            new EmploymentPeriodDto(
                employee.Id,
                hireDate,
                end,
                EmploymentPeriodSource.Hire,
                employee.TerminationReasonId,
                employee.TerminationReasonId is { } reasonId ? reasonNames.GetValueOrDefault(reasonId) : null,
                SeverancePaid: false,
                LeaveSettledAtExit: false,
                days),
        ];
    }
}

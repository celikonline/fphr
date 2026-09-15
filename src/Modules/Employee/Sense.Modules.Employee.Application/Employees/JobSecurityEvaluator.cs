using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Shared.Contracts.Legislation;
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Modules.Employee.Application.Employees;

/// <summary>
/// Result of the job-security scope check ("İş güvencesi kontrolü", 11.3.03 E5a). <see cref="ShowWarning"/> is
/// advisory only - the termination itself is never gated on it (spec: "do not gate the termination itself on this").
/// </summary>
public sealed record JobSecurityEvaluation(bool InScope, bool ShowWarning, string? WarningMessage)
{
    public const string DefaultWarningMessage = "Fesih yazılı yapılmalı, savunma alınmalı.";
}

/// <summary>
/// Evaluates whether a terminated employee falls under 4857 md.18 job-security scope, and whether the chosen
/// termination type should surface the advisory "fesih yazılı yapılmalı, savunma alınmalı" warning.
/// </summary>
public sealed class JobSecurityEvaluator(IEmployeeRepository employees, ILegislationParameterReader legislation, TimeProvider clock)
{
    private const decimal FallbackMinHeadcount = 30m;
    private const decimal FallbackMinSeniorityMonths = 6m;

    public async Task<JobSecurityEvaluation> EvaluateAsync(EmployeeEntity employee, TerminationType terminationType, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var minHeadcount = await legislation.GetDecimalOrDefaultAsync(
            LegislationParameterKeys.JobSecurityMinHeadcount, today, FallbackMinHeadcount, cancellationToken).ConfigureAwait(false);
        var minSeniorityMonths = await legislation.GetDecimalOrDefaultAsync(
            LegislationParameterKeys.JobSecurityMinSeniorityMonths, today, FallbackMinSeniorityMonths, cancellationToken).ConfigureAwait(false);

        var headcount = await employees.CountActiveAsync(cancellationToken).ConfigureAwait(false);
        var seniorityMonths = SeniorityMonths(employee, today);

        // Simplification (11.3.03 spec, noted explicitly): the real rule groups headcount by "iş kolu" (business
        // line) across worksites and excludes "yer altı işçisi" (IsUndergroundWorker) from the seniority requirement.
        // Neither business-line grouping nor a business-line field exists in the codebase yet, so this uses total
        // active tenant headcount and applies the seniority floor uniformly. TODO: revisit once business lines exist.
        var inScope = headcount > minHeadcount
            && seniorityMonths > minSeniorityMonths
            && employee.ContractType == ContractType.Indefinite
            && !employee.IsEmployerRepresentative;

        // Fixed-term contracts are already excluded above (ContractType != Indefinite); İkale and the employee's own
        // just-cause termination are excluded here because only ValidCause_Art18 shows the warning.
        var showWarning = inScope && terminationType == TerminationType.ValidCause_Art18;
        return new JobSecurityEvaluation(inScope, showWarning, showWarning ? JobSecurityEvaluation.DefaultWarningMessage : null);
    }

    /// <summary>Completed months between the employee's seniority start (LeaveAccrualStartDate when earlier than HireDate, else HireDate) and <paramref name="today"/>; 0 when there is no hire date yet.</summary>
    private static decimal SeniorityMonths(EmployeeEntity employee, DateOnly today)
    {
        if (employee.HireDate is not { } hireDate)
        {
            return 0m;
        }

        var start = employee.LeaveAccrualStartDate is { } accrualStart && accrualStart < hireDate ? accrualStart : hireDate;
        if (today < start)
        {
            return 0m;
        }

        var months = ((today.Year - start.Year) * 12) + today.Month - start.Month;
        if (start.AddMonths(months) > today)
        {
            months--;
        }

        return Math.Max(0, months);
    }
}

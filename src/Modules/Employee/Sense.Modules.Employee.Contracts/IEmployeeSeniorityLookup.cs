namespace Sense.Modules.Employee.Contracts;

/// <summary>
/// Which seniority a calculation is for, mirroring Employee.Domain.Employees.SeniorityPurpose ("Kıdem birleştirme",
/// 11.3.03) - a Contracts-only shape so other modules (Payroll's severance/notice estimate, senseik-011 F-06) can
/// read it without depending on Employee's Domain/Infrastructure. Kept in sync by hand, not cast, so the two enums
/// stay free to diverge later (mirrors EmployeePayFrequency/PayFrequency).
/// </summary>
public enum EmployeeSeniorityPurpose
{
    /// <summary>İzin kıdemi.</summary>
    Leave = 0,

    /// <summary>Kıdem tazminatı kıdemi.</summary>
    Severance = 1,

    /// <summary>İş güvencesi kıdemi (4857 md.18).</summary>
    JobSecurity = 2,
}

/// <summary>
/// Read-only cross-module lookup: lets Payroll estimate kıdem/ihbar tazminatı (F-06 "İşten ayrılış sihirbazı") from
/// the employee's employment periods without depending on Employee's Domain or Infrastructure. Mirrors
/// IEmployeeSalaryLookup/IEmployeeBirthDateLookup's shape. Implemented by Sense.Modules.Employee.Infrastructure and
/// registered by EmployeeModule.AddModule; the severance estimate query is API-only (no Worker consumer), so no
/// explicit Sense.Worker registration is needed, unlike IEmployeeSalaryLookup/IEmployeeBirthDateLookup.
/// </summary>
public interface IEmployeeSeniorityLookup
{
    /// <summary>
    /// Days that count towards <paramref name="purpose"/> across all of the employee's employment periods, both ends
    /// inclusive (see Employee.Domain.Employees.Employee.CountedSeniorityDays); 0 when the employee has no hire date.
    /// An employee with no recorded periods falls back to a single implicit period starting at the hire date.
    /// </summary>
    Task<int> GetCountedSeniorityDaysAsync(
        Guid tenantId, Guid employeeId, EmployeeSeniorityPurpose purpose, DateOnly asOf, CancellationToken cancellationToken = default);
}

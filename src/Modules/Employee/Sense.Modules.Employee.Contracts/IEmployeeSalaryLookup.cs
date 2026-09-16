namespace Sense.Modules.Employee.Contracts;

/// <summary>Payment cadence mirroring Employee.Domain.Employees.PayFrequency ("Ödeme düzeni", 11.3.03), exposed as a
/// Contracts-only shape so other modules (Payroll, 11.3.07 "Ücret esası") can read it without depending on Employee's
/// Domain/Infrastructure. Keep in sync with PayFrequency by hand - values are mapped explicitly (not cast) precisely
/// so the two enums are free to diverge later.</summary>
public enum EmployeePayFrequency
{
    Monthly = 0,
    Daily = 1,
    Yearly = 2,
    Weekly = 3,
    Hourly = 4,
}

/// <summary>The pay frequency of the employee's current salary record, and the date it took effect.</summary>
public sealed record EmployeePayPeriod(EmployeePayFrequency PayFrequency, DateOnly EffectiveFrom);

/// <summary>Whether a salary amount is expressed net or gross (11.3.03 "Maaş türü"; Employee.Domain.Employees.SalaryType mirror).</summary>
public enum EmployeeSalaryType
{
    Net = 0,
    Gross = 1,
}

/// <summary>
/// The employee's current salary record content the Payroll calculation engine needs (11.3.07-bordro.md P3 "Dönem
/// hesabı"): amount, currency, whether it is net or gross, plus the pay frequency/effective date already exposed by
/// <see cref="EmployeePayPeriod"/>. Kept as its own read (not folded into EmployeePayPeriod) so the existing P2
/// caller (EmployeeHiredHandler, which only ever needed PayFrequency) is untouched.
/// </summary>
public sealed record EmployeeCurrentSalary(decimal Amount, string Currency, EmployeeSalaryType SalaryType, EmployeePayFrequency PayFrequency, DateOnly EffectiveFrom);

/// <summary>
/// Read-only cross-module lookup: lets Payroll derive <c>EmployeePayrollProfile.WageBasis</c> (11.3.07 "Ücret esası")
/// from the employee's current salary record ("Maaş bloğu" > "Ödeme düzeni") without depending on Employee's Domain
/// or Infrastructure. Implemented by Sense.Modules.Employee.Infrastructure and registered by EmployeeModule.AddModule
/// (and explicitly in Sense.Worker, mirroring IEmployeeContactLookup, since Payroll's EmployeeHired consumer runs there).
/// </summary>
public interface IEmployeeSalaryLookup
{
    /// <summary>The salary record in effect today, or null if the employee has none yet (e.g. hired but not yet salaried).</summary>
    Task<EmployeePayPeriod?> GetCurrentPayPeriodAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>The full current salary record content (amount/currency/net-or-gross), for the Payroll period run
    /// (11.3.07 P3) - amounts are sensitive (employee.salary.view) so this is a Contracts-port read, never carried on
    /// an event. Null if the employee has no salary record yet.</summary>
    Task<EmployeeCurrentSalary?> GetCurrentSalaryAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default);
}

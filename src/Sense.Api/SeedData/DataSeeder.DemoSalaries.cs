using Sense.Modules.Employee.Domain.Employees;
using Sense.Shared.Kernel.ValueObjects;

namespace Sense.Api.SeedData;

public partial class DataSeeder
{
    /// <summary>Asgari ücret (brüt, aylık TRY) - the floor of the demo salary table (interns).</summary>
    private const decimal DemoMinimumGrossSalary = 33_030m;

    private const decimal DemoFallbackGrossSalary = 36_000m;

    /// <summary>Per-employee step on top of a title's base salary, so people with the same title do not all earn the same amount.</summary>
    private const decimal DemoSalaryStep = 1_000m;

    private const int DemoSalaryStepCount = 4;

    /// <summary>Gross monthly base salary (TRY) per demo job title (unit managers and leads are resolved from the department specs).</summary>
    private static readonly Dictionary<string, decimal> DemoTitleSalaries = new(StringComparer.OrdinalIgnoreCase)
    {
        [SeedDataConstants.DemoOwnerTitle] = 140_000m,
        [SeedDataConstants.DemoHrTitle] = 120_000m,
        [SeedDataConstants.DemoAdminTitle] = 105_000m,
        [SeedDataConstants.DemoInternTitle] = DemoMinimumGrossSalary,
        ["Satış Temsilcisi"] = 45_000m,
        ["Kıdemli Satış Temsilcisi"] = 58_000m,
        ["Satış Destek Uzmanı"] = 40_000m,
        ["Yazılım Mühendisi"] = 68_000m,
        ["Kıdemli Yazılım Mühendisi"] = 88_000m,
        ["Test Mühendisi"] = 60_000m,
        ["İK Uzmanı"] = 48_000m,
        ["İşe Alım Uzmanı"] = 46_000m,
        ["Finans Uzmanı"] = 52_000m,
        ["Muhasebe Uzmanı"] = 47_000m,
    };

    private const decimal DemoManagerSalary = 95_000m;

    private const decimal DemoLeadSalary = 78_000m;

    /// <summary>
    /// Demo salary history: one current gross monthly TRY record per active employee, effective from the hire date, so the
    /// payroll period run has a wage to work from (it skips employees without one as "no_salary_record"). Amounts come from a
    /// fixed title table (never from the seeded <c>_random</c>, which would shift the rest of the deterministic demo data).
    /// Added through <see cref="Employee.AddSalaryRecord"/> like the salary endpoint does; no SalaryChanged event is raised.
    /// </summary>
    private void SeedDemoSalaries(IReadOnlyList<DemoEmployee> employees)
    {
        var index = 0;
        var seeded = 0;
        foreach (var (employee, _, title) in employees)
        {
            index++;
            if (employee.Status != EmployeeStatus.Active || employee.HireDate is not { } hireDate)
            {
                continue;
            }

            var amount = DemoGrossSalaryFor(title, index);
            var result = employee.AddSalaryRecord(new SalaryRecordData(
                hireDate, amount, Currencies.TRY, SalaryType.Gross, PayFrequency.Monthly, Note: "Demo maaş kaydı"));
            if (result.IsFailure)
            {
                throw new InvalidOperationException($"Failed to seed demo salary for employee {employee.Id}: {result.Error}");
            }

            seeded++;
        }

        logger.LogInformation("Demo salary records seeded: {SalaryCount} gross monthly salaries", seeded);
    }

    private static decimal DemoGrossSalaryFor(string? title, int employeeIndex)
    {
        if (title is null)
        {
            return DemoFallbackGrossSalary;
        }

        var isIntern = string.Equals(title, SeedDataConstants.DemoInternTitle, StringComparison.OrdinalIgnoreCase);
        var baseAmount = DemoTitleSalaries.TryGetValue(title, out var known) ? known : DemoTitleLevelSalary(title);
        return isIntern ? baseAmount : baseAmount + ((employeeIndex % DemoSalaryStepCount) * DemoSalaryStep);
    }

    private static decimal DemoTitleLevelSalary(string title)
    {
        var department = SeedDataConstants.DemoDepartments.FirstOrDefault(d =>
            string.Equals(d.ManagerTitle, title, StringComparison.OrdinalIgnoreCase) || string.Equals(d.LeadTitle, title, StringComparison.OrdinalIgnoreCase));
        if (department is null)
        {
            return DemoFallbackGrossSalary;
        }

        return string.Equals(department.ManagerTitle, title, StringComparison.OrdinalIgnoreCase) ? DemoManagerSalary : DemoLeadSalary;
    }
}

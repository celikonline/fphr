using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Employee.Contracts;

/// <summary>Employee module permission definitions.</summary>
public static class EmployeePermissions
{
    private const string Module = "employee";
    private const string Group = "Employee";

    public const string EmployeesReadKey = "employee.employees.read";
    public const string EmployeesCreateKey = "employee.employees.create";
    public const string EmployeesUpdateKey = "employee.employees.update";
    public const string EmployeesBulkUpdateKey = "employee.employees.bulk_update";
    public const string DirectoryViewKey = "employee.directory.view";
    public const string OrgChartViewKey = "employee.orgchart.view";
    public const string SalaryViewKey = "employee.salary.view";
    public const string MyViewKey = "employee.my.view";
    public const string EmployeesImportKey = "employee.employees.import";

    /// <summary>Unmasked TCKN and health/disability fields (DisabilityDegree, BloodType, MilitaryStatus); everyone else gets masked/null.</summary>
    public const string SensitiveViewKey = "employee.sensitive.view";

    /// <summary>Read employee records (list/detail), filtered by data scope.</summary>
    public static readonly Permission EmployeesRead = new(
        EmployeesReadKey, Module, "Read Employees", Group, "View employee records");

    /// <summary>Create an employee record.</summary>
    public static readonly Permission EmployeesCreate = new(
        EmployeesCreateKey, Module, "Create Employee", Group, "Create an employee record");

    /// <summary>Update an employee record.</summary>
    public static readonly Permission EmployeesUpdate = new(
        EmployeesUpdateKey, Module, "Update Employee", Group, "Update an employee record");

    /// <summary>Bulk operations over an employee selection (e.g. assign manager).</summary>
    public static readonly Permission EmployeesBulkUpdate = new(
        EmployeesBulkUpdateKey, Module, "Bulk Update Employees", Group, "Run bulk operations over selected employees");

    /// <summary>Company directory (name, unit, work contact).</summary>
    public static readonly Permission DirectoryView = new(
        DirectoryViewKey, Module, "View Directory", Group, "View the company employee directory and upcoming birthdays");

    /// <summary>Organization chart by manager hierarchy.</summary>
    public static readonly Permission OrgChartView = new(
        OrgChartViewKey, Module, "View Org Chart", Group, "View the organization chart");

    /// <summary>Salary and payment information (no consumer yet; granted via unit authorization).</summary>
    public static readonly Permission SalaryView = new(
        SalaryViewKey, Module, "View Salary", Group, "View salary and payment information");

    /// <summary>Own employee card (self-service; matches the "*.my.*" pattern of self-service roles).</summary>
    public static readonly Permission MyView = new(
        MyViewKey, Module, "View Own Employee Card", Group, "View one's own employee record");

    /// <summary>Run the "employee.master" data import template (on top of datatransfer.import.run).</summary>
    public static readonly Permission EmployeesImport = new(
        EmployeesImportKey, Module, "Import Employees", Group, "Insert or update employee records from an import file");

    /// <summary>Unmasked TCKN and health/disability fields.</summary>
    public static readonly Permission SensitiveView = new(
        SensitiveViewKey, Module, "View Sensitive Fields", Group, "View unmasked national id and health/disability fields");

    /// <summary>All Employee module permissions.</summary>
    public static IEnumerable<Permission> All =>
        [EmployeesRead, EmployeesCreate, EmployeesUpdate, EmployeesBulkUpdate, DirectoryView, OrgChartView, SalaryView, MyView, EmployeesImport, SensitiveView];
}

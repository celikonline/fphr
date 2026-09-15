using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Employee.Contracts;

/// <summary>Employee module permission definitions (Faz 0 minimal stub - full personnel permission set is Faz 1).</summary>
public static class EmployeePermissions
{
    private const string Module = "employee";
    private const string Group = "Employee";

    /// <summary>Read minimal employee records (id, display name, status, org unit).</summary>
    public static readonly Permission EmployeesRead = new(
        "employee.employees.read", Module, "Read Employees", Group, "View minimal employee records");

    /// <summary>Create a minimal employee stub record.</summary>
    public static readonly Permission EmployeesCreate = new(
        "employee.employees.create", Module, "Create Employee", Group, "Create a minimal employee stub record");

    /// <summary>All Employee module permissions.</summary>
    public static IEnumerable<Permission> All => [EmployeesRead, EmployeesCreate];
}

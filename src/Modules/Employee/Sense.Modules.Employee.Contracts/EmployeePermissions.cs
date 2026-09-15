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

    /// <summary>Create/update/delete salary records (on top of <see cref="SalaryViewKey"/>).</summary>
    public const string SalaryEditKey = "employee.salary.edit";

    /// <summary>Create/update/delete bank accounts and pick the primary one; reading them needs only employees.read (IBAN masked without sensitive.view).</summary>
    public const string BankAccountsEditKey = "employee.bank_accounts.edit";
    public const string MyViewKey = "employee.my.view";
    public const string EmployeesImportKey = "employee.employees.import";

    /// <summary>Unmasked TCKN, IBAN and health/disability fields (DisabilityDegree, BloodType, MilitaryStatus); everyone else gets masked/null.</summary>
    public const string SensitiveViewKey = "employee.sensitive.view";

    /// <summary>Create/update/deactivate/reactivate custom field definitions (tenant admin catalog).</summary>
    public const string CustomFieldManageKey = "employee.customfield.manage";

    /// <summary>List custom field definitions (on top of employees.read for reading employees' values).</summary>
    public const string CustomFieldViewKey = "employee.customfield.view";

    /// <summary>
    /// Self-service edit of one's own contact/address fields and employee-editable custom field values
    /// ("Self-servis düzenleme", 11.3.03). Contains ".self." so the wildcard role wiring (PermissionPatterns.SelfService)
    /// grants it automatically.
    /// </summary>
    public const string ProfileSelfEditKey = "employee.profile.self.edit";

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

    /// <summary>Salary history (card "Ödemeler" tab); unit managers get it only via the unit authorization's salary switch.</summary>
    public static readonly Permission SalaryView = new(
        SalaryViewKey, Module, "View Salary", Group, "View salary and payment information");

    /// <summary>Maintain salary history.</summary>
    public static readonly Permission SalaryEdit = new(
        SalaryEditKey, Module, "Edit Salary", Group, "Add, edit and delete salary records");

    /// <summary>Maintain bank accounts.</summary>
    public static readonly Permission BankAccountsEdit = new(
        BankAccountsEditKey, Module, "Edit Bank Accounts", Group, "Add, edit and delete bank accounts and choose the primary account");

    /// <summary>Own employee card (self-service; matches the "*.my.*" pattern of self-service roles).</summary>
    public static readonly Permission MyView = new(
        MyViewKey, Module, "View Own Employee Card", Group, "View one's own employee record");

    /// <summary>Run the "employee.master" data import template (on top of datatransfer.import.run).</summary>
    public static readonly Permission EmployeesImport = new(
        EmployeesImportKey, Module, "Import Employees", Group, "Insert or update employee records from an import file");

    /// <summary>Unmasked TCKN, IBAN and health/disability fields.</summary>
    public static readonly Permission SensitiveView = new(
        SensitiveViewKey, Module, "View Sensitive Fields", Group, "View unmasked national id, IBAN and health/disability fields");

    /// <summary>Maintain custom field definitions.</summary>
    public static readonly Permission CustomFieldManage = new(
        CustomFieldManageKey, Module, "Manage Custom Fields", Group, "Create, update, deactivate and reactivate custom field definitions");

    /// <summary>List custom field definitions.</summary>
    public static readonly Permission CustomFieldView = new(
        CustomFieldViewKey, Module, "View Custom Field Definitions", Group, "View the custom field definition catalog");

    /// <summary>Self-service profile edit (own contact fields + own employee-editable custom fields).</summary>
    public static readonly Permission ProfileSelfEdit = new(
        ProfileSelfEditKey, Module, "Edit Own Profile", Group, "Edit one's own contact fields and employee-editable custom fields");

    /// <summary>All Employee module permissions.</summary>
    public static IEnumerable<Permission> All =>
        [
            EmployeesRead, EmployeesCreate, EmployeesUpdate, EmployeesBulkUpdate, DirectoryView, OrgChartView, SalaryView, SalaryEdit,
            BankAccountsEdit, MyView, EmployeesImport, SensitiveView, CustomFieldManage, CustomFieldView, ProfileSelfEdit,
        ];
}

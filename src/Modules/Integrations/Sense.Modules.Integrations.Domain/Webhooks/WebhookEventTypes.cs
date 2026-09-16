namespace Sense.Modules.Integrations.Domain.Webhooks;

/// <summary>
/// Event types an outbound webhook subscription can subscribe to (Faz 8 #61 - "Bordro modülü kapalıyken employee.*
/// event senkronu"). Payloads never carry amounts/IBANs (matching Employee's own integration events); a subscriber
/// syncing a full payroll data set is expected to pull the rest through the Employee API.
/// </summary>
public static class WebhookEventTypes
{
    public const string EmployeeHired = "employee.hired";
    public const string EmployeeTerminated = "employee.terminated";
    public const string EmployeeRehired = "employee.rehired";
    public const string EmployeeSuspended = "employee.suspended";
    public const string EmployeeReactivated = "employee.reactivated";
    public const string EmployeeManagerChanged = "employee.manager_changed";
    public const string EmployeeCareerChanged = "employee.career_changed";
    public const string EmployeeSalaryChanged = "employee.salary_changed";
    public const string EmployeeProfileChanged = "employee.profile_changed";

    public static readonly IReadOnlyCollection<string> All =
    [
        EmployeeHired, EmployeeTerminated, EmployeeRehired, EmployeeSuspended, EmployeeReactivated,
        EmployeeManagerChanged, EmployeeCareerChanged, EmployeeSalaryChanged, EmployeeProfileChanged,
    ];
}

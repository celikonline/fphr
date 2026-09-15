namespace Sense.Modules.Employee.Domain;

/// <summary>Domain error codes for Employee module.</summary>
public static class EmployeeDomainErrors
{
    public const string EmployeeNotFound = "employee.not_found";
    public const string FirstNameRequired = "employee.first_name_required";
    public const string LastNameRequired = "employee.last_name_required";
    public const string NameTooLong = "employee.name_too_long";
    public const string NationalIdInvalid = "employee.national_id_invalid";
    public const string WorkEmailInvalid = "employee.work_email_invalid";
    public const string PersonalEmailInvalid = "employee.personal_email_invalid";
    public const string WorkPhoneTooLong = "employee.work_phone_too_long";
    public const string ManagerSelf = "employee.manager_self";
    public const string ManagerCycle = "employee.manager_cycle";
}

/// <summary>Domain constants and length limits.</summary>
public static class EmployeeLimits
{
    public const int NameMaxLength = 100;

    /// <summary>"First Last": both names at max length plus the separating space.</summary>
    public const int DisplayNameMaxLength = (NameMaxLength * 2) + 1;

    public const int NationalIdLength = 11;
    public const int EmailMaxLength = 254;
    public const int PhoneMaxLength = 30;
}

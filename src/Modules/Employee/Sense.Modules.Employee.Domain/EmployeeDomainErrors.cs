namespace Sense.Modules.Employee.Domain;

/// <summary>Domain error codes for Employee module.</summary>
public static class EmployeeDomainErrors
{
    public const string EmployeeNotFound = "employee.not_found";
    public const string DisplayNameRequired = "employee.display_name_required";
}

/// <summary>Domain constants and length limits.</summary>
public static class EmployeeLimits
{
    public const int DisplayNameMaxLength = 200;
}

namespace Sense.Modules.Assets.Application;

/// <summary>Application-layer error codes (handler-level checks not owned by the domain model).</summary>
public static class AssetsErrors
{
    public const string NoEmployeeForUser = "assets.no_employee_for_user";

    public const string EmployeeNotFound = "assets.employee_not_found";
    public const string IssuedBeforeHire = "assets.issued_before_hire";
}

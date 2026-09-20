namespace Sense.Modules.Employee.Domain.Employees;

/// <summary>
/// First/last names end up in Excel exports, e-mail templates and PDFs, so markup characters and control
/// characters are rejected at the boundary instead of being escaped by every consumer. Everything else stays
/// allowed (apostrophes, hyphens, dots, digits in foreign or legacy names).
/// </summary>
public static class EmployeeNameRules
{
    public static bool IsSafe(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return true;
        }

        foreach (var c in name)
        {
            if (c is '<' or '>' || char.IsControl(c))
            {
                return false;
            }
        }

        return true;
    }
}

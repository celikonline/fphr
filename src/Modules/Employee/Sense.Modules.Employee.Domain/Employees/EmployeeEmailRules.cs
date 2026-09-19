using Sense.Shared.Kernel.ValueObjects;

namespace Sense.Modules.Employee.Domain.Employees;

/// <summary>
/// Employee-module email rule (senseik-044): format-valid (kernel <see cref="EmailAddress"/>) AND ASCII-only. The
/// SMTP envelope (RFC 5321) is ASCII; a Turkish-charactered local-part or domain (ş, ü, ç, ğ, ö, ı) is only
/// deliverable when every hop supports SMTPUTF8/IDN, which is not guaranteed - accepting it silently produces mail
/// that bounces. Rejecting up front (rather than transcoding to punycode) keeps the stored address identical to what
/// the user typed and what mail relays actually see.
/// </summary>
public static class EmployeeEmailRules
{
    public static bool IsValid(string value) => EmailAddress.IsValid(value) && IsAscii(value);

    private static bool IsAscii(string value)
    {
        foreach (var c in value)
        {
            if (!char.IsAscii(c))
            {
                return false;
            }
        }

        return true;
    }
}

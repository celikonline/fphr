using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Domain.Terminations;

namespace Sense.Modules.Employee.Application.Terminations;

/// <summary>Termination reason (catalog row for the wizard's dropdown; web/src pages consume this via GET /employees/termination-reasons).</summary>
/// <param name="AllowedSgkExitCodes">
/// The SGK codes valid for <see cref="DefaultTerminationType"/> (derived from <see cref="SgkExitCodeRules"/>); null
/// when the reason has no termination type or the type imposes no restriction (Other). The web wizard uses this to
/// render a mandatory sub-code picker for the md.24/md.25 families instead of a free-text field.
/// </param>
public sealed record TerminationReasonDto(
    Guid Id,
    string Code,
    string Name,
    string? DefaultSgkExitCode,
    TerminationType? DefaultTerminationType,
    bool IsSystem,
    bool IsActive,
    IReadOnlyList<string>? AllowedSgkExitCodes)
{
    public static TerminationReasonDto From(TerminationReason r) => new(
        r.Id, r.Code, r.Name, r.DefaultSgkExitCode, r.DefaultTerminationType, r.IsSystem, r.IsActive,
        r.DefaultTerminationType is { } type
            ? SgkExitCodeRules.AllowedCodes(type)?.OrderBy(code => code, StringComparer.Ordinal).ToList()
            : null);
}

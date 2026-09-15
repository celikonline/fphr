using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Domain.Terminations;

namespace Sense.Modules.Employee.Application.Terminations;

/// <summary>Termination reason (catalog row for the wizard's dropdown; web/src pages consume this via GET /employees/termination-reasons).</summary>
public sealed record TerminationReasonDto(
    Guid Id,
    string Code,
    string Name,
    string? DefaultSgkExitCode,
    TerminationType? DefaultTerminationType,
    bool IsSystem,
    bool IsActive)
{
    public static TerminationReasonDto From(TerminationReason r) =>
        new(r.Id, r.Code, r.Name, r.DefaultSgkExitCode, r.DefaultTerminationType, r.IsSystem, r.IsActive);
}

using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Domain.Terminals;

/// <summary>Terminal grubu (zaman-devam Z5.1, spec §4): terminalleri lokasyon/bina bazında gruplamak için yalnız bir ad.</summary>
public sealed class TerminalGroup : TenantAggregateRoot<Guid>
{
    private TerminalGroup()
    {
    }

    private TerminalGroup(Guid id, Guid tenantId, string name)
        : base(id, tenantId) => Name = name;

    public string Name { get; private set; } = string.Empty;

    public static Result<TerminalGroup> Create(Guid tenantId, string name)
    {
        if (!IsValidName(name))
        {
            return Error.Validation(AttendanceDomainErrors.TerminalGroupNameRequired);
        }

        return new TerminalGroup(Guid.CreateVersion7(), tenantId, name.Trim());
    }

    public Result Rename(string name)
    {
        if (!IsValidName(name))
        {
            return Error.Validation(AttendanceDomainErrors.TerminalGroupNameRequired);
        }

        Name = name.Trim();
        return Result.Success();
    }

    private static bool IsValidName(string name) => !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= AttendanceLimits.NameMaxLength;
}

using System.Text.RegularExpressions;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Announcements.Domain;

/// <summary>
/// Tenant-defined company event category catalog entry (E4): a name and a display colour (<c>#RRGGBB</c>). Not to be
/// confused with the <see cref="CompanyEventType"/> enum, which classifies the calendar entry kind.
/// </summary>
public sealed partial class CompanyEventCategory : TenantAggregateRoot<Guid>
{
    // ReSharper disable once UnusedMember.Local
    private CompanyEventCategory() { }

    public string Name { get; private set; } = null!;
    public string Color { get; private set; } = null!;

    public static Result<CompanyEventCategory> Create(Guid tenantId, string name, string color)
    {
        var validated = Validate(name, color);
        if (validated.IsFailure)
        {
            return validated.Error;
        }

        return new CompanyEventCategory { Id = Guid.NewGuid(), TenantId = tenantId, Name = name.Trim(), Color = color };
    }

    public Result Update(string name, string color)
    {
        var validated = Validate(name, color);
        if (validated.IsFailure)
        {
            return validated;
        }

        Name = name.Trim();
        Color = color;
        return Result.Success();
    }

    private static Result Validate(string name, string color)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > CompanyEventLimits.CategoryNameMaxLength)
        {
            return Error.Validation(CompanyEventDomainErrors.CategoryNameRequired);
        }

        return color is not null && HexColor().IsMatch(color)
            ? Result.Success()
            : Error.Validation(CompanyEventDomainErrors.CategoryColorInvalid);
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColor();
}

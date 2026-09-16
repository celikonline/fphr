using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Recruitment.Domain.Tags;

/// <summary>
/// A tenant-wide candidate tag catalog entry (roadmap #89 "otomatik etiketleme" - R2 scope is the manual catalog +
/// assignment only; auto-tagging from application-form answers needs the career site's questions, R3).
/// Deactivated instead of deleted since <see cref="Candidates.CandidateTagAssignment"/> rows reference it by id.
/// </summary>
public sealed class Tag : TenantAggregateRoot<Guid>, IAuditLogged
{
    private Tag()
    {
    }

    private Tag(Guid id, Guid tenantId, string name)
        : base(id, tenantId)
    {
        Name = name;
        IsActive = true;
    }

    public string Name { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public static Result<Tag> Create(Guid tenantId, string name, Guid? id = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation(RecruitmentDomainErrors.TagNameRequired);
        }

        return new Tag(id ?? Guid.CreateVersion7(), tenantId, Guard.MaxLength(name.Trim(), RecruitmentLimits.NameMaxLength));
    }

    public Result Deactivate()
    {
        if (!IsActive)
        {
            return Error.Rule(RecruitmentDomainErrors.TagAlreadyInactive);
        }

        IsActive = false;
        return Result.Success();
    }

    public void Reactivate() => IsActive = true;
}

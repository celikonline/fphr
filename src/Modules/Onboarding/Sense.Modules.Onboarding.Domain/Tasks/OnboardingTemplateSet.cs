using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Onboarding.Domain.Tasks;

/// <summary>
/// A named group of task templates for one stage (e.g. "Ofis çalışanı işe alım"). HR starts a whole set for an
/// employee from their profile; each employee can start a given set only once.
/// </summary>
public sealed class OnboardingTemplateSet : TenantAggregateRoot<Guid>, IAuditLogged
{
    private OnboardingTemplateSet()
    {
    }

    private OnboardingTemplateSet(Guid id, Guid tenantId, OnboardingStage stage) : base(id, tenantId)
    {
        Stage = stage;
    }

    public OnboardingStage Stage { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public List<Guid> TemplateIds { get; private set; } = [];

    public bool IsActive { get; private set; } = true;

    public static Result<OnboardingTemplateSet> Create(Guid tenantId, OnboardingStage stage, string name, IEnumerable<Guid> templateIds)
    {
        var set = new OnboardingTemplateSet(Guid.CreateVersion7(), tenantId, stage);
        var applied = set.Update(name, templateIds);
        return applied.IsFailure ? applied.Error : set;
    }

    public Result Update(string name, IEnumerable<Guid> templateIds)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation(OnboardingDomainErrors.TemplateSetNameRequired);
        }

        if (name.Trim().Length > OnboardingLimits.TemplateSetNameMaxLength)
        {
            return Error.Validation(OnboardingDomainErrors.TemplateSetNameTooLong);
        }

        var ids = templateIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return Error.Validation(OnboardingDomainErrors.TemplateSetEmpty);
        }

        Name = name.Trim();
        TemplateIds = ids;
        return Result.Success();
    }

    public void SetActive(bool isActive) => IsActive = isActive;
}

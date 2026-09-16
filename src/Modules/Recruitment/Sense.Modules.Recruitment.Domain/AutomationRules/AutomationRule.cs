using Sense.Modules.Recruitment.Domain.Applications;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Recruitment.Domain.AutomationRules;

/// <summary>
/// A pipeline rule (roadmap #88 `PipelineRule{Trigger, FromStage, ToStage, RequiredFormTemplateId}`): moving a
/// <see cref="Applications.JobApplication"/> from <see cref="FromStage"/> to <see cref="ToStage"/> requires at least
/// one submitted <see cref="FormResponses.RecruitmentFormResponse"/> against <see cref="RequiredFormTemplateId"/> for
/// that application. R2b scope: only this one trigger kind (mandatory-form-on-transition) is modeled; other
/// automation actions from the roadmap doc are out of scope.
/// </summary>
public sealed class AutomationRule : TenantAggregateRoot<Guid>, IAuditLogged
{
    private AutomationRule()
    {
    }

    private AutomationRule(Guid id, Guid tenantId, ApplicationStage fromStage, ApplicationStage toStage, Guid requiredFormTemplateId)
        : base(id, tenantId)
    {
        FromStage = fromStage;
        ToStage = toStage;
        RequiredFormTemplateId = requiredFormTemplateId;
        IsActive = true;
    }

    public ApplicationStage FromStage { get; private set; }

    public ApplicationStage ToStage { get; private set; }

    public Guid RequiredFormTemplateId { get; private set; }

    public bool IsActive { get; private set; }

    public static Result<AutomationRule> Create(Guid tenantId, ApplicationStage fromStage, ApplicationStage toStage, Guid requiredFormTemplateId, Guid? id = null)
    {
        if (fromStage == toStage)
        {
            return Error.Validation(RecruitmentDomainErrors.AutomationRuleSameStage);
        }

        return new AutomationRule(id ?? Guid.CreateVersion7(), tenantId, fromStage, toStage, requiredFormTemplateId);
    }

    public Result Deactivate()
    {
        if (!IsActive)
        {
            return Error.Rule(RecruitmentDomainErrors.AutomationRuleAlreadyInactive);
        }

        IsActive = false;
        return Result.Success();
    }

    public void Reactivate() => IsActive = true;
}

using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Onboarding.Domain.Tasks;

/// <summary>Which side of the employment lifecycle a task template applies to.</summary>
public enum OnboardingStage
{
    Onboarding = 0,
    Offboarding = 1,
}

/// <summary>
/// Tenant-defined checklist item that gets materialized into an <see cref="EmployeeOnboardingTask"/> per employee
/// once an <see cref="OnboardingStage"/> is triggered for them (e.g. hire, termination).
/// </summary>
public sealed class OnboardingTaskTemplate : TenantAggregateRoot<Guid>, IAuditLogged
{
    private OnboardingTaskTemplate()
    {
    }

    private OnboardingTaskTemplate(Guid id, Guid tenantId, OnboardingStage stage) : base(id, tenantId)
    {
        Stage = stage;
        IsActive = true;
    }

    public OnboardingStage Stage { get; private set; }

    public string Title { get; private set; } = string.Empty;

    /// <summary>Role code (e.g. "hr", "it") the task is assigned to; not a user - the roadmap defers per-user
    /// assignment to a later version.</summary>
    public string ResponsibleRoleCode { get; private set; } = string.Empty;

    /// <summary>Days offset from the stage's reference date (hire date for Onboarding, termination date for
    /// Offboarding). Negative means the task is due <em>before</em> the reference date - e.g. the SGK işe giriş
    /// bildirgesi must reach SGK before the employee's actual start date, so its template would use -1.</summary>
    public int DueDateOffsetDays { get; private set; }

    public bool IsMandatory { get; private set; }

    public bool IsActive { get; private set; } = true;

    public static Result<OnboardingTaskTemplate> Create(
        Guid tenantId, OnboardingStage stage, string title, string responsibleRoleCode, int dueDateOffsetDays, bool isMandatory)
    {
        var template = new OnboardingTaskTemplate(Guid.CreateVersion7(), tenantId, stage);
        var applied = template.Apply(title, responsibleRoleCode, dueDateOffsetDays, isMandatory);
        return applied.IsFailure ? applied.Error : template;
    }

    /// <summary>Editable fields only - <see cref="Stage"/> is fixed at creation (a template's aşaması doesn't change).</summary>
    public Result Update(string title, string responsibleRoleCode, int dueDateOffsetDays, bool isMandatory) =>
        Apply(title, responsibleRoleCode, dueDateOffsetDays, isMandatory);

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    private Result Apply(string title, string responsibleRoleCode, int dueDateOffsetDays, bool isMandatory)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Error.Validation(OnboardingDomainErrors.TaskTemplateTitleRequired);
        }

        if (title.Length > OnboardingLimits.TaskTemplateTitleMaxLength)
        {
            return Error.Validation(OnboardingDomainErrors.TaskTemplateTitleTooLong);
        }

        if (string.IsNullOrWhiteSpace(responsibleRoleCode))
        {
            return Error.Validation(OnboardingDomainErrors.TaskTemplateResponsibleRoleRequired);
        }

        Title = title.Trim();
        ResponsibleRoleCode = responsibleRoleCode.Trim();
        DueDateOffsetDays = dueDateOffsetDays;
        IsMandatory = isMandatory;
        return Result.Success();
    }
}

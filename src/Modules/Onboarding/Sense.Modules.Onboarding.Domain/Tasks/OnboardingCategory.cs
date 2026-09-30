using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Onboarding.Domain.Tasks;

/// <summary>Task importance level shown next to a checklist item.</summary>
public enum OnboardingImportance
{
    Normal = 0,
    High = 1,
    Critical = 2,
}

/// <summary>Tenant-defined grouping layer for checklist items (e.g. "Belgeler", "IT"). Templates may be uncategorized.</summary>
public sealed class OnboardingCategory : TenantAggregateRoot<Guid>, IAuditLogged
{
    private OnboardingCategory()
    {
    }

    private OnboardingCategory(Guid id, Guid tenantId) : base(id, tenantId)
    {
    }

    public string Name { get; private set; } = string.Empty;

    public int SortOrder { get; private set; }

    public bool IsActive { get; private set; } = true;

    public static Result<OnboardingCategory> Create(Guid tenantId, string name, int sortOrder)
    {
        var category = new OnboardingCategory(Guid.CreateVersion7(), tenantId);
        var applied = category.Update(name, sortOrder);
        return applied.IsFailure ? applied.Error : category;
    }

    public Result Update(string name, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation(OnboardingDomainErrors.CategoryNameRequired);
        }

        if (name.Trim().Length > OnboardingLimits.CategoryNameMaxLength)
        {
            return Error.Validation(OnboardingDomainErrors.CategoryNameTooLong);
        }

        Name = name.Trim();
        SortOrder = sortOrder;
        return Result.Success();
    }

    public void SetActive(bool isActive) => IsActive = isActive;
}

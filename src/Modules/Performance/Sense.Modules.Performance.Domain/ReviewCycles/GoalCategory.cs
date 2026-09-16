using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Performance.Domain.ReviewCycles;

/// <summary>Kiracı tanımlı hedef kategorisi (ör. "Satış", "Zaman", "Para") - kendi aggregate'i, ReviewCycle'a bağlı değil.</summary>
public sealed class GoalCategory : TenantAggregateRoot<Guid>
{
    private GoalCategory()
    {
    }

    private GoalCategory(Guid id, Guid tenantId, string name) : base(id, tenantId) => Name = name;

    public string Name { get; private set; } = string.Empty;

    public static Result<GoalCategory> Create(Guid tenantId, string name, Guid? id = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation(PerformanceDomainErrors.GoalCategoryNameRequired);
        }

        return new GoalCategory(id ?? Guid.CreateVersion7(), tenantId, Guard.MaxLength(name.Trim(), PerformanceLimits.NameMaxLength));
    }
}

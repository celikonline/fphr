using Sense.Modules.Onboarding.Domain.Tasks;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Onboarding.Domain;

/// <summary>Tenant-scoped task template catalog repository.</summary>
public interface IOnboardingTaskTemplateRepository
{
    void Add(OnboardingTaskTemplate template);

    Task<Result<OnboardingTaskTemplate>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>True when any template of the tenant references the category (blocks category deletion).</summary>
    Task<bool> AnyUsingCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default);

    /// <summary>Every template of the tenant (active and inactive), stage then title order.</summary>
    Task<IReadOnlyList<OnboardingTaskTemplate>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>True when the tenant has at least one template (default templates are seeded only when false).</summary>
    Task<bool> AnyAsync(CancellationToken cancellationToken = default);

    /// <summary>Active templates for the given stage, used to materialize an employee's checklist.</summary>
    Task<IReadOnlyList<OnboardingTaskTemplate>> ListActiveByStageAsync(OnboardingStage stage, CancellationToken cancellationToken = default);
}

/// <summary>Per-employee materialized checklist row repository.</summary>
public interface IEmployeeOnboardingTaskRepository
{
    void Add(EmployeeOnboardingTask task);

    Task<Result<EmployeeOnboardingTask>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Non-null if this employee+template pair was already materialized - used to keep
    /// <c>MaterializeTasksForEmployee</c> idempotent (does not re-create rows on repeated calls).</summary>
    Task<EmployeeOnboardingTask?> FindAsync(Guid employeeId, Guid templateId, CancellationToken cancellationToken = default);

    /// <summary>True when any task of the employee was created by starting the given template set.</summary>
    Task<bool> AnyForSetAsync(Guid employeeId, Guid templateSetId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmployeeOnboardingTask>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>Every still-Pending task of the tenant (HR's open to-do list).</summary>
    Task<IReadOnlyList<EmployeeOnboardingTask>> ListPendingAsync(CancellationToken cancellationToken = default);
}

/// <summary>Tenant-scoped onboarding category repository.</summary>
public interface IOnboardingCategoryRepository
{
    void Add(OnboardingCategory category);

    void Remove(OnboardingCategory category);

    Task<Result<OnboardingCategory>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OnboardingCategory>> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>Tenant-scoped named template set repository.</summary>
public interface IOnboardingTemplateSetRepository
{
    void Add(OnboardingTemplateSet set);

    Task<Result<OnboardingTemplateSet>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OnboardingTemplateSet>> ListAsync(CancellationToken cancellationToken = default);
}

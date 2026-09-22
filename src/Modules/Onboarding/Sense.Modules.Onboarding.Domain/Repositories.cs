using Sense.Modules.Onboarding.Domain.Tasks;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Onboarding.Domain;

/// <summary>Tenant-scoped task template catalog repository.</summary>
public interface IOnboardingTaskTemplateRepository
{
    void Add(OnboardingTaskTemplate template);

    Task<Result<OnboardingTaskTemplate>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

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

    Task<IReadOnlyList<EmployeeOnboardingTask>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>Every still-Pending task of the tenant (HR's open to-do list).</summary>
    Task<IReadOnlyList<EmployeeOnboardingTask>> ListPendingAsync(CancellationToken cancellationToken = default);
}

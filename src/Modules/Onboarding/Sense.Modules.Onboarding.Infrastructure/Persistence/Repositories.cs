using Microsoft.EntityFrameworkCore;
using Sense.Modules.Onboarding.Domain;
using Sense.Modules.Onboarding.Domain.Tasks;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Onboarding.Infrastructure.Persistence;

/// <summary>EF Core task template repository (tenant isolation via ModuleDbContext's global query filter).</summary>
public sealed class OnboardingTaskTemplateRepository(OnboardingDbContext context) : IOnboardingTaskTemplateRepository
{
    public void Add(OnboardingTaskTemplate template) => context.TaskTemplates.Add(template);

    public async Task<Result<OnboardingTaskTemplate>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var template = await context.TaskTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        return template is null ? Error.NotFound(OnboardingDomainErrors.TaskTemplateNotFound) : template;
    }

    public async Task<IReadOnlyList<OnboardingTaskTemplate>> ListAsync(CancellationToken cancellationToken = default) =>
        await context.TaskTemplates
            .OrderBy(t => t.Stage)
            .ThenBy(t => t.DueDateOffsetDays)
            .ThenBy(t => t.Title)
            .ToListAsync(cancellationToken);

    public async Task<bool> AnyAsync(CancellationToken cancellationToken = default) =>
        await context.TaskTemplates.AnyAsync(cancellationToken);

    public async Task<IReadOnlyList<OnboardingTaskTemplate>> ListActiveByStageAsync(
        OnboardingStage stage, CancellationToken cancellationToken = default) =>
        await context.TaskTemplates
            .Where(t => t.Stage == stage && t.IsActive)
            .ToListAsync(cancellationToken);
}

/// <summary>EF Core per-employee checklist repository.</summary>
public sealed class EmployeeOnboardingTaskRepository(OnboardingDbContext context) : IEmployeeOnboardingTaskRepository
{
    public void Add(EmployeeOnboardingTask task) => context.EmployeeTasks.Add(task);

    public async Task<Result<EmployeeOnboardingTask>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var task = await context.EmployeeTasks.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        return task is null ? Error.NotFound(OnboardingDomainErrors.EmployeeTaskNotFound) : task;
    }

    public async Task<EmployeeOnboardingTask?> FindAsync(Guid employeeId, Guid templateId, CancellationToken cancellationToken = default) =>
        await context.EmployeeTasks.FirstOrDefaultAsync(t => t.EmployeeId == employeeId && t.TemplateId == templateId, cancellationToken);

    public async Task<bool> AnyForSetAsync(Guid employeeId, Guid templateSetId, CancellationToken cancellationToken = default) =>
        await context.EmployeeTasks.AnyAsync(t => t.EmployeeId == employeeId && t.TemplateSetId == templateSetId, cancellationToken);

    public async Task<IReadOnlyList<EmployeeOnboardingTask>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        await context.EmployeeTasks
            .Where(t => t.EmployeeId == employeeId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<EmployeeOnboardingTask>> ListPendingAsync(CancellationToken cancellationToken = default) =>
        await context.EmployeeTasks
            .Where(t => t.Status == EmployeeOnboardingTaskStatus.Pending)
            .ToListAsync(cancellationToken);
}

/// <summary>EF Core onboarding category repository.</summary>
public sealed class OnboardingCategoryRepository(OnboardingDbContext context) : IOnboardingCategoryRepository
{
    public void Add(OnboardingCategory category) => context.Categories.Add(category);

    public async Task<Result<OnboardingCategory>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var category = await context.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        return category is null ? Error.NotFound(OnboardingDomainErrors.CategoryNotFound) : category;
    }

    public async Task<IReadOnlyList<OnboardingCategory>> ListAsync(CancellationToken cancellationToken = default) =>
        await context.Categories.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(cancellationToken);
}

/// <summary>EF Core named template set repository.</summary>
public sealed class OnboardingTemplateSetRepository(OnboardingDbContext context) : IOnboardingTemplateSetRepository
{
    public void Add(OnboardingTemplateSet set) => context.TemplateSets.Add(set);

    public async Task<Result<OnboardingTemplateSet>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var set = await context.TemplateSets.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        return set is null ? Error.NotFound(OnboardingDomainErrors.TemplateSetNotFound) : set;
    }

    public async Task<IReadOnlyList<OnboardingTemplateSet>> ListAsync(CancellationToken cancellationToken = default) =>
        await context.TemplateSets.OrderBy(s => s.Stage).ThenBy(s => s.Name).ToListAsync(cancellationToken);
}

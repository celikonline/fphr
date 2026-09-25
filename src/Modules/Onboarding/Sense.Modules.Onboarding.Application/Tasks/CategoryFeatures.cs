using FluentValidation;
using Sense.Modules.Onboarding.Domain;
using Sense.Modules.Onboarding.Domain.Tasks;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Onboarding.Application.Tasks;

public sealed record OnboardingCategoryDto(Guid Id, string Name, int SortOrder, bool IsActive)
{
    public static OnboardingCategoryDto From(OnboardingCategory category) =>
        new(category.Id, category.Name, category.SortOrder, category.IsActive);
}

/// <summary>Done/total of one category for an employee. Waived tasks are excluded from the total; CategoryId null = uncategorized.</summary>
public sealed record OnboardingCategoryProgressDto(Guid? CategoryId, string Name, int Done, int Total)
{
    public int Percent => Total == 0 ? 0 : (int)Math.Round(100.0 * Done / Total);
}

public static class OnboardingCategoryProgress
{
    public static IReadOnlyList<OnboardingCategoryProgressDto> Compute(
        IEnumerable<EmployeeOnboardingTask> tasks, IEnumerable<OnboardingCategory> categories)
    {
        var names = categories.ToDictionary(c => c.Id);
        return tasks
            .Where(t => t.Status != EmployeeOnboardingTaskStatus.Waived)
            .GroupBy(t => t.CategoryId is { } id && names.ContainsKey(id) ? t.CategoryId : null)
            .Select(g => new OnboardingCategoryProgressDto(
                g.Key,
                g.Key is { } id ? names[id].Name : string.Empty,
                g.Count(t => t.Status == EmployeeOnboardingTaskStatus.Completed),
                g.Count()))
            .OrderBy(p => p.CategoryId is { } id ? names[id].SortOrder : int.MaxValue)
            .ThenBy(p => p.Name, StringComparer.CurrentCulture)
            .ToList();
    }
}

[RequiresPermission("onboarding.tasks.view")]
public sealed record ListOnboardingCategoriesQuery : IQuery<IReadOnlyList<OnboardingCategoryDto>>;

public sealed class ListOnboardingCategoriesHandler(IOnboardingCategoryRepository categories)
    : IQueryHandler<ListOnboardingCategoriesQuery, IReadOnlyList<OnboardingCategoryDto>>
{
    public async Task<Result<IReadOnlyList<OnboardingCategoryDto>>> Handle(
        ListOnboardingCategoriesQuery query, CancellationToken cancellationToken) =>
        Result.Success<IReadOnlyList<OnboardingCategoryDto>>(
            (await categories.ListAsync(cancellationToken)).Select(OnboardingCategoryDto.From).ToList());
}

[RequiresPermission("onboarding.templates.manage")]
public sealed record CreateOnboardingCategoryCommand(string Name, int SortOrder) : ICommand<OnboardingCategoryDto>;

public sealed class CreateOnboardingCategoryValidator : AbstractValidator<CreateOnboardingCategoryCommand>
{
    public CreateOnboardingCategoryValidator() =>
        RuleFor(x => x.Name).NotEmpty().MaximumLength(OnboardingLimits.CategoryNameMaxLength);
}

public sealed class CreateOnboardingCategoryHandler(ITenantContext tenant, IOnboardingCategoryRepository categories)
    : ICommandHandler<CreateOnboardingCategoryCommand, OnboardingCategoryDto>
{
    public Task<Result<OnboardingCategoryDto>> Handle(CreateOnboardingCategoryCommand command, CancellationToken cancellationToken)
    {
        var created = OnboardingCategory.Create(tenant.TenantId, command.Name, command.SortOrder);
        if (created.IsFailure)
        {
            return Task.FromResult(Result.Failure<OnboardingCategoryDto>(created.Error));
        }

        categories.Add(created.Value);
        return Task.FromResult(Result.Success(OnboardingCategoryDto.From(created.Value)));
    }
}

[RequiresPermission("onboarding.templates.manage")]
public sealed record UpdateOnboardingCategoryCommand(Guid CategoryId, string Name, int SortOrder, bool IsActive)
    : ICommand<OnboardingCategoryDto>;

public sealed class UpdateOnboardingCategoryValidator : AbstractValidator<UpdateOnboardingCategoryCommand>
{
    public UpdateOnboardingCategoryValidator() =>
        RuleFor(x => x.Name).NotEmpty().MaximumLength(OnboardingLimits.CategoryNameMaxLength);
}

public sealed class UpdateOnboardingCategoryHandler(IOnboardingCategoryRepository categories)
    : ICommandHandler<UpdateOnboardingCategoryCommand, OnboardingCategoryDto>
{
    public async Task<Result<OnboardingCategoryDto>> Handle(UpdateOnboardingCategoryCommand command, CancellationToken cancellationToken)
    {
        var category = await categories.GetByIdAsync(command.CategoryId, cancellationToken);
        if (category.IsFailure)
        {
            return category.Error;
        }

        var updated = category.Value.Update(command.Name, command.SortOrder);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        category.Value.SetActive(command.IsActive);
        return OnboardingCategoryDto.From(category.Value);
    }
}

/// <summary>Per-category done/total for one employee's checklist (waived tasks excluded from the total).</summary>
[RequiresPermission("onboarding.tasks.view")]
public sealed record GetEmployeeCategoryProgressQuery(Guid EmployeeId) : IQuery<IReadOnlyList<OnboardingCategoryProgressDto>>;

public sealed class GetEmployeeCategoryProgressHandler(IEmployeeOnboardingTaskRepository tasks, IOnboardingCategoryRepository categories)
    : IQueryHandler<GetEmployeeCategoryProgressQuery, IReadOnlyList<OnboardingCategoryProgressDto>>
{
    public async Task<Result<IReadOnlyList<OnboardingCategoryProgressDto>>> Handle(
        GetEmployeeCategoryProgressQuery query, CancellationToken cancellationToken) =>
        Result.Success(OnboardingCategoryProgress.Compute(
            await tasks.ListByEmployeeAsync(query.EmployeeId, cancellationToken),
            await categories.ListAsync(cancellationToken)));
}

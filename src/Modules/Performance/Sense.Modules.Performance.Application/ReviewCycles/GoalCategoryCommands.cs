using FluentValidation;
using Sense.Modules.Performance.Domain;
using Sense.Modules.Performance.Domain.ReviewCycles;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Performance.Application.ReviewCycles;

public sealed record GoalCategoryDto(Guid Id, string Name);

/// <summary>Kiracı tanımlı hedef kategorisi ekler (ör. "Satış", "Zaman", "Para").</summary>
[RequiresPermission("performance.goal_category.manage")]
public sealed record CreateGoalCategoryCommand(string Name) : ICommand<Guid>;

public sealed class CreateGoalCategoryValidator : AbstractValidator<CreateGoalCategoryCommand>
{
    public CreateGoalCategoryValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(PerformanceLimits.NameMaxLength);
}

public sealed class CreateGoalCategoryHandler(ITenantContext tenant, IGoalCategoryRepository categories) : ICommandHandler<CreateGoalCategoryCommand, Guid>
{
    public Task<Result<Guid>> Handle(CreateGoalCategoryCommand command, CancellationToken cancellationToken)
    {
        var created = GoalCategory.Create(tenant.TenantId, command.Name);
        if (created.IsFailure)
        {
            return Task.FromResult(Result.Failure<Guid>(created.Error));
        }

        categories.Add(created.Value);
        return Task.FromResult(Result.Success(created.Value.Id));
    }
}

/// <summary>Hedef kategorisi listesi (Hedef ekleme diyaloğundaki seçim listesi).</summary>
[RequiresPermission("performance.goals.create")]
public sealed record ListGoalCategoriesQuery : IQuery<IReadOnlyList<GoalCategoryDto>>;

public sealed class ListGoalCategoriesHandler(IGoalCategoryRepository categories) : IQueryHandler<ListGoalCategoriesQuery, IReadOnlyList<GoalCategoryDto>>
{
    public async Task<Result<IReadOnlyList<GoalCategoryDto>>> Handle(ListGoalCategoriesQuery query, CancellationToken cancellationToken)
    {
        var list = await categories.ListAsync(cancellationToken);
        IReadOnlyList<GoalCategoryDto> dtos = list.Select(c => new GoalCategoryDto(c.Id, c.Name)).ToList();
        return Result.Success(dtos);
    }
}

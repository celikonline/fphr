using Sense.Modules.Onboarding.Domain;
using Sense.Modules.Onboarding.Domain.Tasks;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Onboarding.Application.Tasks;

/// <summary>A checklist row joined with its template's title/role/mandatory flag, plus the derived overdue flag.</summary>
public sealed record OnboardingChecklistItemDto(
    Guid Id,
    Guid EmployeeId,
    Guid TemplateId,
    string Title,
    string ResponsibleRoleCode,
    bool IsMandatory,
    OnboardingStage Stage,
    DateOnly DueDate,
    EmployeeOnboardingTaskStatus Status,
    bool IsOverdue,
    DateTime? CompletedAt,
    string? WaivedReason,
    DateTime? WaivedAt);

internal static class OnboardingChecklist
{
    public static async Task<IReadOnlyList<OnboardingChecklistItemDto>> ToItemsAsync(
        IEnumerable<EmployeeOnboardingTask> tasks, IOnboardingTaskTemplateRepository templates, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var byId = (await templates.ListAsync(cancellationToken)).ToDictionary(t => t.Id);
        return tasks
            .Select(task =>
            {
                byId.TryGetValue(task.TemplateId, out var template);
                return new OnboardingChecklistItemDto(
                    task.Id, task.EmployeeId, task.TemplateId, template?.Title ?? string.Empty,
                    template?.ResponsibleRoleCode ?? string.Empty, template?.IsMandatory ?? false, task.Stage, task.DueDate,
                    task.Status, task.IsOverdue(today), task.CompletedAt, task.WaivedReason, task.WaivedAt);
            })
            .OrderBy(i => i.DueDate)
            .ThenBy(i => i.Title, StringComparer.CurrentCulture)
            .ToList();
    }
}

/// <summary>One employee's whole checklist (every stage and status), due date order.</summary>
[RequiresPermission("onboarding.tasks.view")]
public sealed record ListEmployeeOnboardingTasksQuery(Guid EmployeeId) : IQuery<IReadOnlyList<OnboardingChecklistItemDto>>;

public sealed class ListEmployeeOnboardingTasksHandler(
    IEmployeeOnboardingTaskRepository tasks, IOnboardingTaskTemplateRepository templates, TimeProvider clock)
    : IQueryHandler<ListEmployeeOnboardingTasksQuery, IReadOnlyList<OnboardingChecklistItemDto>>
{
    public async Task<Result<IReadOnlyList<OnboardingChecklistItemDto>>> Handle(
        ListEmployeeOnboardingTasksQuery query, CancellationToken cancellationToken) =>
        Result.Success(await OnboardingChecklist.ToItemsAsync(
            await tasks.ListByEmployeeAsync(query.EmployeeId, cancellationToken), templates, clock, cancellationToken));
}

/// <summary>Every still-pending task across the tenant (HR's to-do list), soonest due first.</summary>
[RequiresPermission("onboarding.tasks.view")]
public sealed record ListPendingOnboardingTasksQuery : IQuery<IReadOnlyList<OnboardingChecklistItemDto>>;

public sealed class ListPendingOnboardingTasksHandler(
    IEmployeeOnboardingTaskRepository tasks, IOnboardingTaskTemplateRepository templates, TimeProvider clock)
    : IQueryHandler<ListPendingOnboardingTasksQuery, IReadOnlyList<OnboardingChecklistItemDto>>
{
    public async Task<Result<IReadOnlyList<OnboardingChecklistItemDto>>> Handle(
        ListPendingOnboardingTasksQuery query, CancellationToken cancellationToken) =>
        Result.Success(await OnboardingChecklist.ToItemsAsync(
            await tasks.ListPendingAsync(cancellationToken), templates, clock, cancellationToken));
}

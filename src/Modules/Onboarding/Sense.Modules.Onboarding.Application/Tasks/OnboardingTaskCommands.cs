using FluentValidation;
using Sense.Modules.Onboarding.Application;
using Sense.Modules.Onboarding.Contracts.Events;
using Sense.Modules.Onboarding.Domain;
using Sense.Modules.Onboarding.Domain.Tasks;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Onboarding.Application.Tasks;

[RequiresPermission("onboarding.templates.manage")]
public sealed record CreateOnboardingTaskTemplateCommand(
    OnboardingStage Stage, string Title, string ResponsibleRoleCode, int DueDateOffsetDays, bool IsMandatory)
    : ICommand<OnboardingTaskTemplateDto>;

public sealed class CreateOnboardingTaskTemplateValidator : AbstractValidator<CreateOnboardingTaskTemplateCommand>
{
    public CreateOnboardingTaskTemplateValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(OnboardingLimits.TaskTemplateTitleMaxLength);
        RuleFor(x => x.ResponsibleRoleCode).NotEmpty();
    }
}

public sealed class CreateOnboardingTaskTemplateHandler(ITenantContext tenant, IOnboardingTaskTemplateRepository templates)
    : ICommandHandler<CreateOnboardingTaskTemplateCommand, OnboardingTaskTemplateDto>
{
    public Task<Result<OnboardingTaskTemplateDto>> Handle(CreateOnboardingTaskTemplateCommand command, CancellationToken cancellationToken)
    {
        var created = OnboardingTaskTemplate.Create(
            tenant.TenantId, command.Stage, command.Title, command.ResponsibleRoleCode, command.DueDateOffsetDays, command.IsMandatory);
        if (created.IsFailure)
        {
            return Task.FromResult(Result.Failure<OnboardingTaskTemplateDto>(created.Error));
        }

        templates.Add(created.Value);
        return Task.FromResult(Result.Success(OnboardingTaskTemplateDto.From(created.Value)));
    }
}

/// <summary>Materializes one <see cref="EmployeeOnboardingTask"/> per active template of the given stage for the
/// employee. Idempotent: templates already materialized for this employee (via <c>IEmployeeOnboardingTaskRepository.FindAsync</c>)
/// are skipped, so re-triggering the same stage (e.g. a retried event handler) does not duplicate rows.</summary>
[RequiresPermission("onboarding.tasks.manage")]
public sealed record MaterializeTasksForEmployeeCommand(Guid EmployeeId, OnboardingStage Stage, DateOnly ReferenceDate)
    : ICommand<IReadOnlyList<EmployeeOnboardingTaskDto>>;

public sealed class MaterializeTasksForEmployeeValidator : AbstractValidator<MaterializeTasksForEmployeeCommand>
{
    public MaterializeTasksForEmployeeValidator() => RuleFor(x => x.EmployeeId).NotEmpty();
}

public sealed class MaterializeTasksForEmployeeHandler(
    ITenantContext tenant, IOnboardingTaskTemplateRepository templates, IEmployeeOnboardingTaskRepository tasks, IIntegrationEventOutbox outbox)
    : ICommandHandler<MaterializeTasksForEmployeeCommand, IReadOnlyList<EmployeeOnboardingTaskDto>>
{
    public async Task<Result<IReadOnlyList<EmployeeOnboardingTaskDto>>> Handle(
        MaterializeTasksForEmployeeCommand command, CancellationToken cancellationToken)
    {
        var created = await OnboardingTaskMaterializer.MaterializeAsync(
            tenant.TenantId, templates, tasks, outbox, command.EmployeeId, command.Stage, command.ReferenceDate, cancellationToken);
        return Result.Success<IReadOnlyList<EmployeeOnboardingTaskDto>>(created.Select(EmployeeOnboardingTaskDto.From).ToList());
    }
}

[RequiresPermission("onboarding.tasks.manage")]
public sealed record CompleteTaskCommand(Guid TaskId) : ICommand<EmployeeOnboardingTaskDto>;

public sealed class CompleteTaskValidator : AbstractValidator<CompleteTaskCommand>
{
    public CompleteTaskValidator() => RuleFor(x => x.TaskId).NotEmpty();
}

public sealed class CompleteTaskHandler(
    ICurrentUser currentUser, IEmployeeOnboardingTaskRepository tasks, IOnboardingTaskTemplateRepository templates, IIntegrationEventOutbox outbox)
    : ICommandHandler<CompleteTaskCommand, EmployeeOnboardingTaskDto>
{
    public async Task<Result<EmployeeOnboardingTaskDto>> Handle(CompleteTaskCommand command, CancellationToken cancellationToken)
    {
        var task = await tasks.GetByIdAsync(command.TaskId, cancellationToken);
        if (task.IsFailure)
        {
            return task.Error;
        }

        var completed = task.Value.Complete(currentUser.UserId ?? Guid.Empty);
        if (completed.IsFailure)
        {
            return completed.Error;
        }

        await OnboardingTaskNotices.EnqueueCompletedAsync(templates, outbox, task.Value, cancellationToken);
        return EmployeeOnboardingTaskDto.From(task.Value);
    }
}

[RequiresPermission("onboarding.tasks.manage")]
public sealed record WaiveTaskCommand(Guid TaskId, string Reason) : ICommand<EmployeeOnboardingTaskDto>;

public sealed class WaiveTaskValidator : AbstractValidator<WaiveTaskCommand>
{
    public WaiveTaskValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(OnboardingLimits.WaivedReasonMaxLength);
    }
}

public sealed class WaiveTaskHandler(ICurrentUser currentUser, IEmployeeOnboardingTaskRepository tasks)
    : ICommandHandler<WaiveTaskCommand, EmployeeOnboardingTaskDto>
{
    public async Task<Result<EmployeeOnboardingTaskDto>> Handle(WaiveTaskCommand command, CancellationToken cancellationToken)
    {
        var task = await tasks.GetByIdAsync(command.TaskId, cancellationToken);
        if (task.IsFailure)
        {
            return task.Error;
        }

        var waived = task.Value.Waive(command.Reason, currentUser.UserId ?? Guid.Empty);
        return waived.IsFailure ? waived.Error : EmployeeOnboardingTaskDto.From(task.Value);
    }
}

/// <summary>Self-service: the caller completes one of their own materialized tasks.</summary>
[RequiresPermission("onboarding.my.tasks")]
public sealed record CompleteMyTaskCommand(Guid TaskId) : ICommand<EmployeeOnboardingTaskDto>;

public sealed class CompleteMyTaskValidator : AbstractValidator<CompleteMyTaskCommand>
{
    public CompleteMyTaskValidator() => RuleFor(x => x.TaskId).NotEmpty();
}

public sealed class CompleteMyTaskHandler(
    ICurrentUser currentUser, IEmployeeOnboardingTaskRepository tasks, IOnboardingTaskTemplateRepository templates, IIntegrationEventOutbox outbox)
    : ICommandHandler<CompleteMyTaskCommand, EmployeeOnboardingTaskDto>
{
    public async Task<Result<EmployeeOnboardingTaskDto>> Handle(CompleteMyTaskCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(OnboardingErrors.NoEmployeeForUser);
        }

        var task = await tasks.GetByIdAsync(command.TaskId, cancellationToken);
        if (task.IsFailure)
        {
            return task.Error;
        }

        if (task.Value.EmployeeId != employeeId)
        {
            return Error.Forbidden(OnboardingErrors.NotOwner);
        }

        var completed = task.Value.Complete(currentUser.UserId ?? Guid.Empty);
        if (completed.IsFailure)
        {
            return completed.Error;
        }

        await OnboardingTaskNotices.EnqueueCompletedAsync(templates, outbox, task.Value, cancellationToken);
        return EmployeeOnboardingTaskDto.From(task.Value);
    }
}

/// <summary>Shared by <see cref="CompleteTaskHandler"/> and <see cref="CompleteMyTaskHandler"/>: looks up the
/// completed task's template for its title/responsible role and enqueues the notice. A missing template (should not
/// happen - templates aren't deleted, only deactivated) is not fatal to the completion itself.</summary>
internal static class OnboardingTaskNotices
{
    public static async Task EnqueueCompletedAsync(
        IOnboardingTaskTemplateRepository templates, IIntegrationEventOutbox outbox, EmployeeOnboardingTask task, CancellationToken cancellationToken)
    {
        var template = await templates.GetByIdAsync(task.TemplateId, cancellationToken);
        if (template.IsFailure)
        {
            return;
        }

        outbox.Enqueue(new OnboardingTaskCompleted(
            task.TenantId, task.Id, task.EmployeeId, template.Value.ResponsibleRoleCode, template.Value.Title));
    }
}

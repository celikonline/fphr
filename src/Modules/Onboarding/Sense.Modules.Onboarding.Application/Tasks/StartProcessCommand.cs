using FluentValidation;
using Sense.Modules.Onboarding.Domain;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Onboarding.Application.Tasks;

/// <summary>
/// Starts a named template set (işe alım / ayrılış) for an employee from their profile: materializes the set's
/// active templates, dated from <paramref name="ReferenceDate"/>. A set can be started only once per employee.
/// </summary>
[RequiresPermission("onboarding.tasks.manage")]
public sealed record StartProcessCommand(Guid EmployeeId, Guid TemplateSetId, DateOnly ReferenceDate)
    : ICommand<IReadOnlyList<EmployeeOnboardingTaskDto>>;

public sealed class StartProcessValidator : AbstractValidator<StartProcessCommand>
{
    public StartProcessValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.TemplateSetId).NotEmpty();
    }
}

public sealed class StartProcessHandler(
    ITenantContext tenant,
    IOnboardingTemplateSetRepository sets,
    IOnboardingTaskTemplateRepository templates,
    IEmployeeOnboardingTaskRepository tasks,
    IIntegrationEventOutbox outbox)
    : ICommandHandler<StartProcessCommand, IReadOnlyList<EmployeeOnboardingTaskDto>>
{
    public async Task<Result<IReadOnlyList<EmployeeOnboardingTaskDto>>> Handle(
        StartProcessCommand command, CancellationToken cancellationToken)
    {
        var set = await sets.GetByIdAsync(command.TemplateSetId, cancellationToken);
        if (set.IsFailure)
        {
            return set.Error;
        }

        if (!set.Value.IsActive)
        {
            return Error.Conflict(OnboardingDomainErrors.TemplateSetInactive);
        }

        if (await tasks.AnyForSetAsync(command.EmployeeId, set.Value.Id, cancellationToken))
        {
            return Error.Conflict(OnboardingDomainErrors.TemplateSetAlreadyStarted);
        }

        var ids = set.Value.TemplateIds.ToHashSet();
        var setTemplates = (await templates.ListAsync(cancellationToken)).Where(t => ids.Contains(t.Id) && t.IsActive).ToList();
        var created = await OnboardingTaskMaterializer.MaterializeFromAsync(
            tenant.TenantId, setTemplates, tasks, outbox, command.EmployeeId, set.Value.Stage, command.ReferenceDate,
            set.Value.Id, cancellationToken);
        return Result.Success<IReadOnlyList<EmployeeOnboardingTaskDto>>(created.Select(EmployeeOnboardingTaskDto.From).ToList());
    }
}

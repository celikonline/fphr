using FluentValidation;
using Sense.Modules.Onboarding.Domain;
using Sense.Modules.Onboarding.Domain.Tasks;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Onboarding.Application.Tasks;

/// <summary>Every template (active and inactive), grouped client-side by stage.</summary>
[RequiresPermission("onboarding.tasks.view")]
public sealed record ListOnboardingTaskTemplatesQuery : IQuery<IReadOnlyList<OnboardingTaskTemplateDto>>;

public sealed class ListOnboardingTaskTemplatesHandler(IOnboardingTaskTemplateRepository templates)
    : IQueryHandler<ListOnboardingTaskTemplatesQuery, IReadOnlyList<OnboardingTaskTemplateDto>>
{
    public async Task<Result<IReadOnlyList<OnboardingTaskTemplateDto>>> Handle(
        ListOnboardingTaskTemplatesQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyList<OnboardingTaskTemplateDto> items = (await templates.ListAsync(cancellationToken))
            .Select(OnboardingTaskTemplateDto.From)
            .ToList();
        return Result.Success(items);
    }
}

[RequiresPermission("onboarding.templates.manage")]
public sealed record UpdateOnboardingTaskTemplateCommand(
    Guid TemplateId, string Title, string ResponsibleRoleCode, int DueDateOffsetDays, bool IsMandatory)
    : ICommand<OnboardingTaskTemplateDto>;

public sealed class UpdateOnboardingTaskTemplateValidator : AbstractValidator<UpdateOnboardingTaskTemplateCommand>
{
    public UpdateOnboardingTaskTemplateValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(OnboardingLimits.TaskTemplateTitleMaxLength);
        RuleFor(x => x.ResponsibleRoleCode).NotEmpty().MaximumLength(OnboardingLimits.ResponsibleRoleCodeMaxLength);
    }
}

public sealed class UpdateOnboardingTaskTemplateHandler(IOnboardingTaskTemplateRepository templates)
    : ICommandHandler<UpdateOnboardingTaskTemplateCommand, OnboardingTaskTemplateDto>
{
    public async Task<Result<OnboardingTaskTemplateDto>> Handle(
        UpdateOnboardingTaskTemplateCommand command, CancellationToken cancellationToken)
    {
        var template = await templates.GetByIdAsync(command.TemplateId, cancellationToken);
        if (template.IsFailure)
        {
            return template.Error;
        }

        var updated = template.Value.Update(command.Title, command.ResponsibleRoleCode, command.DueDateOffsetDays, command.IsMandatory);
        return updated.IsFailure ? updated.Error : OnboardingTaskTemplateDto.From(template.Value);
    }
}

/// <summary>Activates or deactivates a template; inactive templates are skipped by future materializations only.</summary>
[RequiresPermission("onboarding.templates.manage")]
public sealed record SetOnboardingTaskTemplateActiveCommand(Guid TemplateId, bool IsActive) : ICommand<OnboardingTaskTemplateDto>;

public sealed class SetOnboardingTaskTemplateActiveHandler(IOnboardingTaskTemplateRepository templates)
    : ICommandHandler<SetOnboardingTaskTemplateActiveCommand, OnboardingTaskTemplateDto>
{
    public async Task<Result<OnboardingTaskTemplateDto>> Handle(
        SetOnboardingTaskTemplateActiveCommand command, CancellationToken cancellationToken)
    {
        var template = await templates.GetByIdAsync(command.TemplateId, cancellationToken);
        if (template.IsFailure)
        {
            return template.Error;
        }

        if (command.IsActive)
        {
            template.Value.Activate();
        }
        else
        {
            template.Value.Deactivate();
        }

        return OnboardingTaskTemplateDto.From(template.Value);
    }
}

/// <summary>Loads the recommended templates when the tenant has none; returns how many were added (0 if any existed).</summary>
[RequiresPermission("onboarding.templates.manage")]
public sealed record SeedDefaultOnboardingTemplatesCommand : ICommand<int>;

public sealed class SeedDefaultOnboardingTemplatesHandler(ITenantContext tenant, IOnboardingTaskTemplateRepository templates)
    : ICommandHandler<SeedDefaultOnboardingTemplatesCommand, int>
{
    public async Task<Result<int>> Handle(SeedDefaultOnboardingTemplatesCommand command, CancellationToken cancellationToken) =>
        await DefaultOnboardingTemplates.EnsureSeededAsync(tenant.TenantId, templates, cancellationToken);
}

using FluentValidation;
using Sense.Modules.Onboarding.Domain;
using Sense.Modules.Onboarding.Domain.Tasks;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Onboarding.Application.Tasks;

public sealed record OnboardingTemplateSetDto(Guid Id, OnboardingStage Stage, string Name, IReadOnlyList<Guid> TemplateIds, bool IsActive)
{
    public static OnboardingTemplateSetDto From(OnboardingTemplateSet set) =>
        new(set.Id, set.Stage, set.Name, set.TemplateIds.ToList(), set.IsActive);
}

internal static class TemplateSetGuard
{
    /// <summary>Every id must be a template of the set's stage; returns the error code of the first violation.</summary>
    public static async Task<Result> ValidateTemplatesAsync(
        IOnboardingTaskTemplateRepository templates, OnboardingStage stage, IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var known = (await templates.ListAsync(cancellationToken)).Where(t => t.Stage == stage).Select(t => t.Id).ToHashSet();
        return ids.All(known.Contains) ? Result.Success() : Error.Validation(OnboardingDomainErrors.TemplateSetUnknownTemplate);
    }
}

[RequiresPermission("onboarding.tasks.view")]
public sealed record ListOnboardingTemplateSetsQuery : IQuery<IReadOnlyList<OnboardingTemplateSetDto>>;

public sealed class ListOnboardingTemplateSetsHandler(IOnboardingTemplateSetRepository sets)
    : IQueryHandler<ListOnboardingTemplateSetsQuery, IReadOnlyList<OnboardingTemplateSetDto>>
{
    public async Task<Result<IReadOnlyList<OnboardingTemplateSetDto>>> Handle(
        ListOnboardingTemplateSetsQuery query, CancellationToken cancellationToken) =>
        Result.Success<IReadOnlyList<OnboardingTemplateSetDto>>(
            (await sets.ListAsync(cancellationToken)).Select(OnboardingTemplateSetDto.From).ToList());
}

[RequiresPermission("onboarding.templates.manage")]
public sealed record CreateOnboardingTemplateSetCommand(OnboardingStage Stage, string Name, IReadOnlyList<Guid> TemplateIds)
    : ICommand<OnboardingTemplateSetDto>;

public sealed class CreateOnboardingTemplateSetValidator : AbstractValidator<CreateOnboardingTemplateSetCommand>
{
    public CreateOnboardingTemplateSetValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(OnboardingLimits.TemplateSetNameMaxLength);
        RuleFor(x => x.TemplateIds).NotEmpty();
    }
}

public sealed class CreateOnboardingTemplateSetHandler(
    ITenantContext tenant, IOnboardingTemplateSetRepository sets, IOnboardingTaskTemplateRepository templates)
    : ICommandHandler<CreateOnboardingTemplateSetCommand, OnboardingTemplateSetDto>
{
    public async Task<Result<OnboardingTemplateSetDto>> Handle(CreateOnboardingTemplateSetCommand command, CancellationToken cancellationToken)
    {
        var known = await TemplateSetGuard.ValidateTemplatesAsync(templates, command.Stage, command.TemplateIds, cancellationToken);
        if (known.IsFailure)
        {
            return known.Error;
        }

        var created = OnboardingTemplateSet.Create(tenant.TenantId, command.Stage, command.Name, command.TemplateIds);
        if (created.IsFailure)
        {
            return created.Error;
        }

        sets.Add(created.Value);
        return OnboardingTemplateSetDto.From(created.Value);
    }
}

[RequiresPermission("onboarding.templates.manage")]
public sealed record UpdateOnboardingTemplateSetCommand(Guid SetId, string Name, IReadOnlyList<Guid> TemplateIds, bool IsActive)
    : ICommand<OnboardingTemplateSetDto>;

public sealed class UpdateOnboardingTemplateSetValidator : AbstractValidator<UpdateOnboardingTemplateSetCommand>
{
    public UpdateOnboardingTemplateSetValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(OnboardingLimits.TemplateSetNameMaxLength);
        RuleFor(x => x.TemplateIds).NotEmpty();
    }
}

public sealed class UpdateOnboardingTemplateSetHandler(IOnboardingTemplateSetRepository sets, IOnboardingTaskTemplateRepository templates)
    : ICommandHandler<UpdateOnboardingTemplateSetCommand, OnboardingTemplateSetDto>
{
    public async Task<Result<OnboardingTemplateSetDto>> Handle(UpdateOnboardingTemplateSetCommand command, CancellationToken cancellationToken)
    {
        var set = await sets.GetByIdAsync(command.SetId, cancellationToken);
        if (set.IsFailure)
        {
            return set.Error;
        }

        var known = await TemplateSetGuard.ValidateTemplatesAsync(templates, set.Value.Stage, command.TemplateIds, cancellationToken);
        if (known.IsFailure)
        {
            return known.Error;
        }

        var updated = set.Value.Update(command.Name, command.TemplateIds);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        set.Value.SetActive(command.IsActive);
        return OnboardingTemplateSetDto.From(set.Value);
    }
}

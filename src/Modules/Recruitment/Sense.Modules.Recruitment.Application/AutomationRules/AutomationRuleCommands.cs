using FluentValidation;
using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Domain.Applications;
using Sense.Modules.Recruitment.Domain.AutomationRules;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Recruitment.Application.AutomationRules;

/// <summary>Creates a pipeline rule requiring a form before an application may move from one stage to another (roadmap #88).</summary>
[RequiresPermission("recruitment.automation_rules.manage")]
public sealed record CreateAutomationRuleCommand(string FromStage, string ToStage, Guid RequiredFormTemplateId) : ICommand<Guid>;

public sealed class CreateAutomationRuleValidator : AbstractValidator<CreateAutomationRuleCommand>
{
    public CreateAutomationRuleValidator()
    {
        RuleFor(x => x.FromStage).IsEnumName(typeof(ApplicationStage), caseSensitive: false);
        RuleFor(x => x.ToStage).IsEnumName(typeof(ApplicationStage), caseSensitive: false);
        RuleFor(x => x.RequiredFormTemplateId).NotEmpty();
    }
}

public sealed class CreateAutomationRuleHandler(ITenantContext tenant, IAutomationRuleRepository rules, IRecruitmentFormTemplateRepository templates)
    : ICommandHandler<CreateAutomationRuleCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateAutomationRuleCommand command, CancellationToken cancellationToken)
    {
        var template = await templates.GetByIdAsync(command.RequiredFormTemplateId, cancellationToken);
        if (template.IsFailure)
        {
            return template.Error;
        }

        var created = AutomationRule.Create(
            tenant.TenantId, Enum.Parse<ApplicationStage>(command.FromStage, ignoreCase: true), Enum.Parse<ApplicationStage>(command.ToStage, ignoreCase: true), command.RequiredFormTemplateId);
        if (created.IsFailure)
        {
            return created.Error;
        }

        rules.Add(created.Value);
        return created.Value.Id;
    }
}

/// <summary>Deactivates a pipeline rule.</summary>
[RequiresPermission("recruitment.automation_rules.manage")]
public sealed record DeactivateAutomationRuleCommand(Guid RuleId) : ICommand;

public sealed class DeactivateAutomationRuleHandler(IAutomationRuleRepository rules) : ICommandHandler<DeactivateAutomationRuleCommand>
{
    public async Task<Result> Handle(DeactivateAutomationRuleCommand command, CancellationToken cancellationToken)
    {
        var existing = await rules.GetByIdAsync(command.RuleId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var result = existing.Value.Deactivate();
        return result.IsFailure ? result.Error : Result.Success();
    }
}

/// <summary>Reactivates a pipeline rule.</summary>
[RequiresPermission("recruitment.automation_rules.manage")]
public sealed record ReactivateAutomationRuleCommand(Guid RuleId) : ICommand;

public sealed class ReactivateAutomationRuleHandler(IAutomationRuleRepository rules) : ICommandHandler<ReactivateAutomationRuleCommand>
{
    public async Task<Result> Handle(ReactivateAutomationRuleCommand command, CancellationToken cancellationToken)
    {
        var existing = await rules.GetByIdAsync(command.RuleId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        existing.Value.Reactivate();
        return Result.Success();
    }
}

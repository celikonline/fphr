namespace Sense.Modules.Recruitment.Application.AutomationRules;

/// <summary>Read model for an <see cref="Sense.Modules.Recruitment.Domain.AutomationRules.AutomationRule"/>.</summary>
public sealed record AutomationRuleDto(Guid Id, string FromStage, string ToStage, Guid RequiredFormTemplateId, bool IsActive);

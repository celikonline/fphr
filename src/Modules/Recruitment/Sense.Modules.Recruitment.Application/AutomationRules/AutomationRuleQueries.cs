using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Domain.AutomationRules;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Recruitment.Application.AutomationRules;

/// <summary>Lists pipeline automation rules, optionally active only.</summary>
[RequiresPermission("recruitment.automation_rules.read")]
public sealed record ListAutomationRulesQuery(bool ActiveOnly, int Take = PagingDefaults.MaxPageSize) : IQuery<IReadOnlyList<AutomationRuleDto>>;

public sealed class ListAutomationRulesHandler(IAutomationRuleRepository rules) : IQueryHandler<ListAutomationRulesQuery, IReadOnlyList<AutomationRuleDto>>
{
    public async Task<Result<IReadOnlyList<AutomationRuleDto>>> Handle(ListAutomationRulesQuery query, CancellationToken cancellationToken)
    {
        var list = await rules.ListAsync(query.ActiveOnly, query.Take, cancellationToken);
        IReadOnlyList<AutomationRuleDto> dtos = list.Select(AutomationRuleMapper.ToDto).ToList();
        return Result.Success(dtos);
    }
}

internal static class AutomationRuleMapper
{
    public static AutomationRuleDto ToDto(AutomationRule rule) =>
        new(rule.Id, rule.FromStage.ToString(), rule.ToStage.ToString(), rule.RequiredFormTemplateId, rule.IsActive);
}

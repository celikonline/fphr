using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Domain.JobRequisitions;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Recruitment.Application.JobRequisitions;

/// <summary>
/// A department's effective JobRequisition field rules (roadmap #87): always returns all
/// <see cref="RequisitionFormField"/> values, resolved against the department's template if one exists, otherwise
/// the original fixed behavior - see <see cref="RequisitionFieldTemplate.Resolve"/>.
/// </summary>
[RequiresPermission("recruitment.requisitions.read")]
public sealed record GetRequisitionFieldTemplateQuery(Guid OrgUnitId) : IQuery<IReadOnlyList<RequisitionFieldRuleDto>>;

public sealed class GetRequisitionFieldTemplateHandler(IRequisitionFieldTemplateRepository templates)
    : IQueryHandler<GetRequisitionFieldTemplateQuery, IReadOnlyList<RequisitionFieldRuleDto>>
{
    public async Task<Result<IReadOnlyList<RequisitionFieldRuleDto>>> Handle(GetRequisitionFieldTemplateQuery query, CancellationToken cancellationToken)
    {
        var template = await templates.FindByOrgUnitAsync(query.OrgUnitId, cancellationToken);

        IReadOnlyList<RequisitionFieldRuleDto> effective = Enum.GetValues<RequisitionFormField>()
            .Select(field => RequisitionFieldTemplate.Resolve(field, template))
            .Select(rule => new RequisitionFieldRuleDto(rule.Field.ToString(), rule.IsVisible, rule.IsRequired, rule.DefaultValue))
            .ToList();

        return Result.Success(effective);
    }
}

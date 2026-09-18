using Sense.Modules.OccupationalSafety.Domain;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.OccupationalSafety.Application.RiskAssessments;

/// <summary>All recorded risk assessments across locations, newest AssessmentDate first.</summary>
[RequiresPermission("ohs.risk_assessments.view")]
public sealed record ListRiskAssessmentsQuery : IQuery<IReadOnlyList<RiskAssessmentDto>>;

public sealed class ListRiskAssessmentsHandler(IRiskAssessmentRepository assessments)
    : IQueryHandler<ListRiskAssessmentsQuery, IReadOnlyList<RiskAssessmentDto>>
{
    public async Task<Result<IReadOnlyList<RiskAssessmentDto>>> Handle(ListRiskAssessmentsQuery query, CancellationToken cancellationToken)
    {
        var list = await assessments.ListAsync(cancellationToken);
        IReadOnlyList<RiskAssessmentDto> dtos = list.Select(RiskAssessmentMapper.ToDto).ToList();
        return Result.Success(dtos);
    }
}

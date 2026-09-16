using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Domain.FormResponses;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Recruitment.Application.FormResponses;

/// <summary>Lists an application's submitted form responses.</summary>
[RequiresPermission("recruitment.form_responses.read")]
public sealed record ListFormResponsesByApplicationQuery(Guid JobApplicationId) : IQuery<IReadOnlyList<FormResponseDto>>;

public sealed class ListFormResponsesByApplicationHandler(IRecruitmentFormResponseRepository responses)
    : IQueryHandler<ListFormResponsesByApplicationQuery, IReadOnlyList<FormResponseDto>>
{
    public async Task<Result<IReadOnlyList<FormResponseDto>>> Handle(ListFormResponsesByApplicationQuery query, CancellationToken cancellationToken)
    {
        var list = await responses.ListByJobApplicationAsync(query.JobApplicationId, cancellationToken);
        IReadOnlyList<FormResponseDto> dtos = list.Select(FormResponseMapper.ToDto).ToList();
        return Result.Success(dtos);
    }
}

internal static class FormResponseMapper
{
    public static FormResponseDto ToDto(RecruitmentFormResponse response) =>
        new(
            response.Id, response.TemplateId, response.JobApplicationId, response.SubmittedByEmployeeId, response.SubmittedAt, response.WeightedScore,
            response.Answers.Select(a => new FieldAnswerDto(a.FieldId, a.Value)).ToList());
}

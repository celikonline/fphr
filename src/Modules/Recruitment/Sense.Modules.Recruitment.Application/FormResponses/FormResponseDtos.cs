namespace Sense.Modules.Recruitment.Application.FormResponses;

/// <summary>One field's answer (read model).</summary>
public sealed record FieldAnswerDto(Guid FieldId, string Value);

/// <summary>Read model for a <see cref="Sense.Modules.Recruitment.Domain.FormResponses.RecruitmentFormResponse"/>.</summary>
public sealed record FormResponseDto(
    Guid Id, Guid TemplateId, Guid JobApplicationId, Guid SubmittedByEmployeeId, DateTime SubmittedAt, decimal? WeightedScore, IReadOnlyList<FieldAnswerDto> Answers);

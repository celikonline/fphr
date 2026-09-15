namespace Sense.Modules.Recruitment.Application.Applications;

/// <summary>Read model for a <see cref="Sense.Modules.Recruitment.Domain.Applications.JobApplication"/>.</summary>
public sealed record JobApplicationDto(
    Guid Id, Guid JobPostingId, Guid CandidateId, string Stage, DateTime AppliedAt, DateTime? ArchivedAt);

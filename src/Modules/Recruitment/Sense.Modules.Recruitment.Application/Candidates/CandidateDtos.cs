namespace Sense.Modules.Recruitment.Application.Candidates;

/// <summary>Read model for a <see cref="Sense.Modules.Recruitment.Domain.Candidates.Candidate"/>.</summary>
public sealed record CandidateDto(Guid Id, string FirstName, string LastName, string Email, string? Phone, string Source);

using Sense.Modules.Recruitment.Domain.Candidates;

namespace Sense.Modules.Recruitment.Application.Candidates;

/// <summary>
/// Read model for a <see cref="Sense.Modules.Recruitment.Domain.Candidates.Candidate"/>. <see cref="Source"/> is kept
/// as the real <see cref="CandidateSource"/> enum (not <c>.ToString()</c>'d to a plain string) so the API's global
/// <c>JsonStringEnumConverter(CamelCase)</c> serializes it camelCase (e.g. "manual"), matching the web's locale keys.
/// </summary>
public sealed record CandidateDto(
    Guid Id, string FirstName, string LastName, string Email, string? Phone, CandidateSource Source, IReadOnlyList<Guid> TagIds,
    bool IsAnonymized);

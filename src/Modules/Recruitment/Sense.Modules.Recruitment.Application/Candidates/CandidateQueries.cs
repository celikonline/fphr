using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Domain.Candidates;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Recruitment.Application.Candidates;

/// <summary>Lists candidates, optionally filtered by a name/email search term.</summary>
[RequiresPermission("recruitment.candidates.read")]
public sealed record ListCandidatesQuery(string? Search) : IQuery<IReadOnlyList<CandidateDto>>;

public sealed class ListCandidatesHandler(ICandidateRepository candidates) : IQueryHandler<ListCandidatesQuery, IReadOnlyList<CandidateDto>>
{
    public async Task<Result<IReadOnlyList<CandidateDto>>> Handle(ListCandidatesQuery query, CancellationToken cancellationToken)
    {
        var list = await candidates.ListAsync(query.Search, cancellationToken);
        IReadOnlyList<CandidateDto> dtos = list.Select(CandidateMapper.ToDto).ToList();
        return Result.Success(dtos);
    }
}

/// <summary>A single candidate by id.</summary>
[RequiresPermission("recruitment.candidates.read")]
public sealed record GetCandidateQuery(Guid CandidateId) : IQuery<CandidateDto>;

public sealed class GetCandidateHandler(ICandidateRepository candidates) : IQueryHandler<GetCandidateQuery, CandidateDto>
{
    public async Task<Result<CandidateDto>> Handle(GetCandidateQuery query, CancellationToken cancellationToken)
    {
        var existing = await candidates.GetByIdAsync(query.CandidateId, cancellationToken);
        return existing.IsFailure ? existing.Error : CandidateMapper.ToDto(existing.Value);
    }
}

internal static class CandidateMapper
{
    public static CandidateDto ToDto(Candidate candidate) =>
        new(
            candidate.Id, candidate.FirstName, candidate.LastName, candidate.Email, candidate.Phone, candidate.Source.ToString(),
            candidate.Tags.Select(t => t.TagId).ToList(), candidate.IsAnonymized);
}

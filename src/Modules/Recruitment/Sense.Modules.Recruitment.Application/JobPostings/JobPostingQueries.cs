using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Domain.JobPostings;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Recruitment.Application.JobPostings;

/// <summary>Lists job postings, optionally filtered to one status.</summary>
[RequiresPermission("recruitment.postings.read")]
public sealed record ListJobPostingsQuery(string? Status) : IQuery<IReadOnlyList<JobPostingDto>>;

public sealed class ListJobPostingsHandler(IJobPostingRepository postings) : IQueryHandler<ListJobPostingsQuery, IReadOnlyList<JobPostingDto>>
{
    public async Task<Result<IReadOnlyList<JobPostingDto>>> Handle(ListJobPostingsQuery query, CancellationToken cancellationToken)
    {
        JobPostingStatus? status = string.IsNullOrWhiteSpace(query.Status) ? null : Enum.Parse<JobPostingStatus>(query.Status);
        var list = await postings.ListAsync(status, cancellationToken);
        IReadOnlyList<JobPostingDto> dtos = list.Select(JobPostingMapper.ToDto).ToList();
        return Result.Success(dtos);
    }
}

/// <summary>A single job posting by id.</summary>
[RequiresPermission("recruitment.postings.read")]
public sealed record GetJobPostingQuery(Guid PostingId) : IQuery<JobPostingDto>;

public sealed class GetJobPostingHandler(IJobPostingRepository postings) : IQueryHandler<GetJobPostingQuery, JobPostingDto>
{
    public async Task<Result<JobPostingDto>> Handle(GetJobPostingQuery query, CancellationToken cancellationToken)
    {
        var existing = await postings.GetByIdAsync(query.PostingId, cancellationToken);
        return existing.IsFailure ? existing.Error : JobPostingMapper.ToDto(existing.Value);
    }
}

internal static class JobPostingMapper
{
    public static JobPostingDto ToDto(JobPosting posting) =>
        new(
            posting.Id,
            posting.Title,
            posting.OrgUnitId,
            posting.LocationId,
            posting.EmploymentType.ToString(),
            posting.Description,
            posting.CompanyNotes,
            posting.OwnerUserId,
            posting.HiringManagerUserId,
            posting.RequisitionId,
            posting.Status.ToString(),
            posting.ApplySlug,
            posting.ClosingDate,
            posting.PublishedAt,
            posting.ArchivedAt,
            posting.ArchiveReason?.ToString(),
            posting.Access.Select(a => a.UserId).ToList());
}

using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Domain.Applications;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Recruitment.Application.Applications;

/// <summary>Lists a job posting's applications.</summary>
[RequiresPermission("recruitment.applications.read")]
public sealed record ListApplicationsByPostingQuery(Guid JobPostingId, int Take = PagingDefaults.MaxPageSize) : IQuery<IReadOnlyList<JobApplicationDto>>;

public sealed class ListApplicationsByPostingHandler(IJobApplicationRepository applications)
    : IQueryHandler<ListApplicationsByPostingQuery, IReadOnlyList<JobApplicationDto>>
{
    public async Task<Result<IReadOnlyList<JobApplicationDto>>> Handle(ListApplicationsByPostingQuery query, CancellationToken cancellationToken)
    {
        var list = await applications.ListByJobPostingAsync(query.JobPostingId, query.Take, cancellationToken);
        IReadOnlyList<JobApplicationDto> dtos = list.Select(JobApplicationMapper.ToDto).ToList();
        return Result.Success(dtos);
    }
}

/// <summary>Lists a candidate's applications (their history across postings).</summary>
[RequiresPermission("recruitment.applications.read")]
public sealed record ListApplicationsByCandidateQuery(Guid CandidateId, int Take = PagingDefaults.MaxPageSize) : IQuery<IReadOnlyList<JobApplicationDto>>;

public sealed class ListApplicationsByCandidateHandler(IJobApplicationRepository applications)
    : IQueryHandler<ListApplicationsByCandidateQuery, IReadOnlyList<JobApplicationDto>>
{
    public async Task<Result<IReadOnlyList<JobApplicationDto>>> Handle(ListApplicationsByCandidateQuery query, CancellationToken cancellationToken)
    {
        var list = await applications.ListByCandidateAsync(query.CandidateId, query.Take, cancellationToken);
        IReadOnlyList<JobApplicationDto> dtos = list.Select(JobApplicationMapper.ToDto).ToList();
        return Result.Success(dtos);
    }
}

internal static class JobApplicationMapper
{
    public static JobApplicationDto ToDto(JobApplication application) =>
        new(application.Id, application.JobPostingId, application.CandidateId, application.Stage.ToString(), application.AppliedAt, application.ArchivedAt);
}

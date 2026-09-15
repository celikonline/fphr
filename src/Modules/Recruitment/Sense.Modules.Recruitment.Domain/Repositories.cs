using Sense.Modules.Recruitment.Domain.Applications;
using Sense.Modules.Recruitment.Domain.Candidates;
using Sense.Modules.Recruitment.Domain.JobPostings;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Recruitment.Domain;

/// <summary>JobPosting aggregate repository.</summary>
public interface IJobPostingRepository
{
    void Add(JobPosting posting);

    Task<Result<JobPosting>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JobPosting>> ListAsync(JobPostingStatus? status = null, CancellationToken cancellationToken = default);
}

/// <summary>Candidate aggregate repository.</summary>
public interface ICandidateRepository
{
    void Add(Candidate candidate);

    Task<Result<Candidate>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Candidate>> ListAsync(string? search = null, CancellationToken cancellationToken = default);
}

/// <summary>JobApplication aggregate repository.</summary>
public interface IJobApplicationRepository
{
    void Add(JobApplication application);

    Task<Result<JobApplication>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JobApplication>> ListByJobPostingAsync(Guid jobPostingId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JobApplication>> ListByCandidateAsync(Guid candidateId, CancellationToken cancellationToken = default);
}

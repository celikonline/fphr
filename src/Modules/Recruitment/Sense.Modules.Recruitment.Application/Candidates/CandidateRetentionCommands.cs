using FluentValidation;
using Sense.Modules.Recruitment.Domain;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Recruitment.Application.Candidates;

/// <summary>
/// KVKK "unutulma hakkı" (right to erasure): HR anonymizes one candidate's personal data on request, ahead of the
/// automatic retention deadline (see <see cref="AnonymizeExpiredCandidatesCommand"/>). Same permission as editing
/// a candidate - erasure is itself a mutation of contact info (to nothing).
/// </summary>
[RequiresPermission("recruitment.candidates.manage")]
public sealed record AnonymizeCandidateCommand(Guid CandidateId) : ICommand;

public sealed class AnonymizeCandidateValidator : AbstractValidator<AnonymizeCandidateCommand>
{
    public AnonymizeCandidateValidator() => RuleFor(x => x.CandidateId).NotEmpty();
}

public sealed class AnonymizeCandidateHandler(ICandidateRepository candidates) : ICommandHandler<AnonymizeCandidateCommand>
{
    public async Task<Result> Handle(AnonymizeCandidateCommand command, CancellationToken cancellationToken)
    {
        var existing = await candidates.GetByIdAsync(command.CandidateId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var anonymized = existing.Value.Anonymize();
        return anonymized.IsFailure ? anonymized.Error : Result.Success();
    }
}

/// <summary>
/// KVKK retention sweep (roadmap "KVKK uyumlu aday verisi saklama ve imha"): anonymizes every candidate of the
/// current tenant whose <see cref="RecruitmentLimits.CandidateDataRetentionMonths"/> window has elapsed. Tenant-
/// scoped by design (mirrors <see cref="Sense.Shared.Contracts.BackgroundJobs.IBackgroundJobService"/>'s per-tenant
/// job model) so it can be wired to a recurring background job per tenant once the shared <c>JobType</c> enum grows
/// a value for it (a <c>Sense.Shared.Contracts</c> change - out of this module's own scope, left for a later slice).
/// Until then HR (or an ops script) triggers this manually; running it twice is safe/idempotent, it only touches
/// rows that are still due.
/// </summary>
[RequiresPermission("recruitment.candidates.manage")]
public sealed record AnonymizeExpiredCandidatesCommand : ICommand<int>;

public sealed class AnonymizeExpiredCandidatesHandler(ICandidateRepository candidates, TimeProvider timeProvider)
    : ICommandHandler<AnonymizeExpiredCandidatesCommand, int>
{
    public async Task<Result<int>> Handle(AnonymizeExpiredCandidatesCommand command, CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow().UtcDateTime.AddMonths(-RecruitmentLimits.CandidateDataRetentionMonths);
        var expired = await candidates.ListRetentionExpiredAsync(cutoff, cancellationToken);

        var anonymizedCount = 0;
        foreach (var candidate in expired)
        {
            if (candidate.Anonymize().IsSuccess)
            {
                anonymizedCount++;
            }
        }

        return anonymizedCount;
    }
}

using FluentValidation;
using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Domain.Applications;
using Sense.Modules.Recruitment.Domain.Candidates;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Recruitment.Application.Public;

/// <summary>
/// Submits a candidate's self-service application from the public career site (R3, roadmap "Kariyer sayfası ve
/// başvuru formu"). Deliberately no <c>[RequiresPermission]</c> - this is the ATS's one anonymous write.
/// Tenant is resolved from the posting (see <see cref="IJobPostingRepository.GetPublicByApplySlugAsync"/>),
/// never from the request; the KVKK consent/aydınlatma metni acceptance itself is Faz 1's ConsentRecord (not yet
/// wired here - see BLOKER.md if still outstanding when this ships to web).
/// </summary>
public sealed record SubmitCareerApplicationCommand(
    string ApplySlug, string FirstName, string LastName, string Email, string? Phone) : ICommand<Guid>;

public sealed class SubmitCareerApplicationValidator : AbstractValidator<SubmitCareerApplicationCommand>
{
    public SubmitCareerApplicationValidator()
    {
        RuleFor(x => x.ApplySlug).NotEmpty().MaximumLength(RecruitmentLimits.ApplySlugLength);
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(RecruitmentLimits.NameMaxLength);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(RecruitmentLimits.NameMaxLength);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(RecruitmentLimits.EmailMaxLength);
        RuleFor(x => x.Phone).MaximumLength(RecruitmentLimits.PhoneMaxLength);
    }
}

public sealed class SubmitCareerApplicationHandler(
    IJobPostingRepository postings, ICandidateRepository candidates, IJobApplicationRepository applications,
    ITenantContextSetter tenantSetter)
    : ICommandHandler<SubmitCareerApplicationCommand, Guid>
{
    public async Task<Result<Guid>> Handle(SubmitCareerApplicationCommand command, CancellationToken cancellationToken)
    {
        var posting = await postings.GetPublicByApplySlugAsync(command.ApplySlug, cancellationToken);
        if (posting.IsFailure)
        {
            return posting.Error;
        }

        // Anonymous request: no JWT tenant claim, so RequestContextMiddleware never opened a tenant scope.
        // Mirrors Identity's AcceptInvitationHandler - resolve tenant from the entity, then scope the rest of the
        // handler to it so every subsequent repository call is correctly tenant-filtered.
        using var scope = tenantSetter.BeginScope(posting.Value.TenantId);

        var existingCandidate = await candidates.FindByEmailAsync(command.Email.Trim(), cancellationToken);
        Guid candidateId;
        if (existingCandidate is null)
        {
            var created = Candidate.Create(
                posting.Value.TenantId, command.FirstName, command.LastName, command.Email, command.Phone,
                source: CandidateSource.CareerSite);
            if (created.IsFailure)
            {
                return created.Error;
            }

            candidates.Add(created.Value);
            candidateId = created.Value.Id;
        }
        else
        {
            candidateId = existingCandidate.Id;
        }

        if (await applications.ExistsAsync(posting.Value.Id, candidateId, cancellationToken))
        {
            return Error.Conflict(RecruitmentDomainErrors.ApplicationAlreadyExists);
        }

        var application = JobApplication.Create(posting.Value.TenantId, posting.Value.Id, candidateId);
        applications.Add(application);
        return application.Id;
    }
}

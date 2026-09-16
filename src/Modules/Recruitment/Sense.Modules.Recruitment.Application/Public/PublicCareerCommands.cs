using FluentValidation;
using Microsoft.Extensions.Logging;
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
/// never from the request. <c>PrivacyNoticeVersion</c>/<c>KvkkAccepted</c> are the aydınlatma metni onayı (Faz 9
/// tur 3 TALIMAT madde 2): validated here and logged, but **not persisted to a new column** - Faz 1's
/// <c>IConsentRecorder</c>/<c>ConsentRecord</c> (S-3, `identity.consent_records`) is the real sahibi; once that
/// port lands in Identity.Contracts this handler should call it instead of only logging. No migration this slice.
/// </summary>
public sealed record SubmitCareerApplicationCommand(
    string ApplySlug, string FirstName, string LastName, string Email, string? Phone,
    string PrivacyNoticeVersion, bool KvkkAccepted) : ICommand<Guid>;

public sealed class SubmitCareerApplicationValidator : AbstractValidator<SubmitCareerApplicationCommand>
{
    public SubmitCareerApplicationValidator()
    {
        RuleFor(x => x.ApplySlug).NotEmpty().MaximumLength(RecruitmentLimits.ApplySlugLength);
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(RecruitmentLimits.NameMaxLength);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(RecruitmentLimits.NameMaxLength);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(RecruitmentLimits.EmailMaxLength);
        RuleFor(x => x.Phone).MaximumLength(RecruitmentLimits.PhoneMaxLength);
        RuleFor(x => x.PrivacyNoticeVersion).NotEmpty().MaximumLength(RecruitmentLimits.PrivacyNoticeVersionMaxLength);
        RuleFor(x => x.KvkkAccepted).Equal(true);
    }
}

public sealed class SubmitCareerApplicationHandler(
    IJobPostingRepository postings, ICandidateRepository candidates, IJobApplicationRepository applications,
    ITenantContextSetter tenantSetter, ILogger<SubmitCareerApplicationHandler> logger)
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

        // KVKK aydınlatma metni onayı: doğrulandı (validator) ama Faz 1'in IConsentRecorder'ı henüz yok, bu yüzden
        // kalıcı bir consent kaydı/kolonu YOK - yalnızca denetim izi için logluyoruz. TALIMAT madde 2.
        logger.LogInformation(
            "Kariyer sitesi başvurusu: adayId={CandidateId}, başvuruId={ApplicationId}, ilanId={JobPostingId}, " +
            "aydınlatmaMetniVersiyonu={PrivacyNoticeVersion}, kvkkOnaylandı={KvkkAccepted}",
            candidateId, application.Id, posting.Value.Id, command.PrivacyNoticeVersion, command.KvkkAccepted);

        return application.Id;
    }
}
